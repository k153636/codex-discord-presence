using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CodexDiscordPresence.Tests;

public sealed class ClaudeCodeHookTransportTests
{
    [Fact]
    public async Task Hook_NativeJapanesePayload_MatchesStatusLineSessionAndRotatesUsage_WithoutChangingExitStatus()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ClaudeHookTransport_" + Guid.NewGuid().ToString("N"), "日本語 ' project");
        Directory.CreateDirectory(directory);
        try
        {
            var executable = Path.Combine(directory, "echo.exe");
            var compile = "$ProgressPreference = 'SilentlyContinue'; Add-Type -TypeDefinition 'public class Echo { public static int Main() { System.Console.OpenStandardInput().CopyTo(System.Console.OpenStandardOutput()); return 7; } }' -OutputType ConsoleApplication -OutputAssembly '" +
                executable.Replace("'", "''", StringComparison.Ordinal) + "'";
            using var compileOutput = new MemoryStream();
            using var compileError = new MemoryStream();
            Assert.Equal(0, await ClaudeCodeStatusLineCommand.RelayAsync(Encoded(compile), [], compileOutput, compileError));
            Assert.Empty(compileError.ToArray());
            Assert.True(File.Exists(executable));

            var now = new DateTimeOffset(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);
            var transcript = Path.Combine(directory, "transcript.jsonl");
            File.WriteAllText(transcript, "{\"type\":\"assistant\",\"sessionId\":\"main\",\"message\":{\"id\":\"a\",\"usage\":{\"input_tokens\":12000,\"output_tokens\":400}}}\n");
            var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
            {
                session_id = "main", cwd = directory, hook_event_name = "Stop", transcript_path = transcript
            }, new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
            Assert.Contains("日本語", Encoding.UTF8.GetString(payload));
            var command = ClaudeCodeHookInstaller.CreateDefinition(executable)["hooks"]![0]!["command"]!.GetValue<string>();
            using var output = new MemoryStream();
            using var error = new MemoryStream();
            Assert.Equal(0, await ClaudeCodeStatusLineCommand.RelayAsync(command, payload, output, error));
            Assert.Equal(payload, output.ToArray());
            Assert.Empty(error.ToArray());

            var hook = ClaudeCodeHookParser.Parse(Encoding.UTF8.GetString(output.ToArray()), now)!;
            Assert.Equal(directory, hook.ProjectPath);
            var observations = new ClaudeCodeObservationStore(Path.Combine(directory, "observations"));
            observations.Write(hook);
            var session = observations.Select(directory, now, TimeSpan.FromMinutes(1))!;
            var usageStore = new ClaudeCodeUsageStore(Path.Combine(directory, "usage"));
            usageStore.Write(ClaudeCodeUsageObservation.Parse(JsonSerializer.Serialize(new
            {
                session_id = "main", cwd = directory,
                rate_limits = new { five_hour = new { used_percentage = 25, resets_at = now.AddHours(3).ToUnixTimeSeconds() } }
            }), now)!);
            Assert.NotNull(usageStore.GetForSession(session, now));
            var tokens = new ClaudeCodeTokenUsageProvider(usageStore).GetSnapshot(session, new(), now);
            Assert.Equal(12_400, tokens.TotalTokens);
            var context = ClaudeCodePresenceProjection.CreateContext(session,
                new("project", directory, null, null, 0, 0, 0, []), new(false, 0, null),
                new(now.UtcDateTime, TimeSpan.Zero), tokenUsage: tokens);
            var current = now.UtcDateTime;
            var renderer = new PresenceTemplateRenderer(() => current);
            var template = new PresenceTemplateOptions { Details = "{ModelName} • {Tokens}", WaitingDetails = "{Cost} {BillingType}{RateLimitDetails}", State = "{ActivityLine}" };
            Assert.Contains("12.4K Token", renderer.Render(template, context).Details);
            current = now.AddSeconds(5).UtcDateTime;
            var details = renderer.Render(template, context).Details;
            Assert.Contains("subsc", details);
            Assert.Contains("25%", details);
            Assert.Contains("3h 0m", details);
            current = now.AddSeconds(10).UtcDateTime;
            Assert.Contains("12.4K Token", renderer.Render(template, context).Details);
        }
        finally { Directory.Delete(Path.GetDirectoryName(directory)!, true); }
    }

    [Fact]
    public void Installer_LegacyOwnedTextBridge_IsUpgradedAndRemoved_WhileUserHooksSurvive()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ClaudeHookUpgrade_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var settings = Path.Combine(directory, "settings.json");
            var owned = Path.Combine(directory, "owned");
            Directory.CreateDirectory(owned);
            var legacy = new JsonObject { ["hooks"] = new JsonArray(new JsonObject
            {
                ["type"] = "command", ["timeout"] = 5,
                ["command"] = Encoded("# CodexDiscordPresence.ClaudeCodeHook.v1\n$payload = [Console]::In.ReadToEnd(); $payload | & 'C:/old/rpc.exe' --claude-hook; exit 0")
            }) };
            var user = JsonNode.Parse("{\"hooks\":[{\"type\":\"command\",\"command\":\"user-command\"}]}")!;
            File.WriteAllText(settings, new JsonObject { ["hooks"] = new JsonObject { ["Stop"] = new JsonArray(user.DeepClone(), legacy.DeepClone()) } }.ToJsonString());
            File.WriteAllText(Path.Combine(owned, "hook-owner.json"), new JsonObject
            {
                ["owner"] = ClaudeCodeNativeCommand.HookOwnershipMarker, ["definition"] = legacy.DeepClone()
            }.ToJsonString());
            var installer = new ClaudeCodeHookInstaller(settings, owned, "C:/new/rpc.exe");
            installer.Install();
            var stop = JsonNode.Parse(File.ReadAllText(settings))!["hooks"]!["Stop"]!.AsArray();
            Assert.Equal(2, stop.Count);
            Assert.True(JsonNode.DeepEquals(user, stop[0]));
            Assert.True(JsonNode.DeepEquals(ClaudeCodeHookInstaller.CreateDefinition("C:/new/rpc.exe"), stop[1]));
            installer.Uninstall();
            stop = JsonNode.Parse(File.ReadAllText(settings))!["hooks"]!["Stop"]!.AsArray();
            Assert.Single(stop);
            Assert.True(JsonNode.DeepEquals(user, stop[0]));
        }
        finally { Directory.Delete(directory, true); }
    }

    private static string Encoded(string script) =>
        "powershell.exe -NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand " +
        Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
}
