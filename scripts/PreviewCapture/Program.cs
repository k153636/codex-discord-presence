using System.Drawing;
using System.Windows.Forms;

namespace CodexDiscordPresence.PreviewCapture;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        var outputPath = Path.GetFullPath(args.Length > 0 ? args[0] : Path.Combine("Preview", "dashboard-test.png"));
        var provider = args.Length > 1 ? args[1] : ProviderIds.Codex;
        if (provider == "claude") provider = ProviderIds.ClaudeCode;
        if (provider is not (ProviderIds.Codex or ProviderIds.ClaudeCode or ProviderIds.Antigravity or "none"))
            throw new ArgumentException("Use codex, claude-code, antigravity, or none.", nameof(args));
        var connection = args.Length > 2 ? args[2] : "connected";
        if (connection is not ("connected" or "connecting" or "disconnected" or "unacknowledged" or "disabled"))
            throw new ArgumentException("Unknown connection fixture.", nameof(args));
        var dpi = args.Length > 3 ? int.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture) : 96;
        if (dpi is < 96 or > 288) throw new ArgumentOutOfRangeException(nameof(dpi));
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var runtime = new PresenceRuntimeState { Enabled = connection != "disabled" };
        runtime.InitializeProviderEnabled(new Dictionary<string, bool>
        {
            [ProviderIds.Codex] = true, [ProviderIds.ClaudeCode] = true, [ProviderIds.Antigravity] = true
        });
        runtime.PublishDashboardSnapshot(CreateShowcaseSnapshot(provider, connection));
        using var form = new CodexDashboardForm(runtime, new PresenceStateStore(), outputPath + ".fixture-state.json");
        var browser = (Microsoft.Web.WebView2.WinForms.WebView2)form.Controls[0];
        Exception? failure = null;
        form.Shown += async (_, _) =>
        {
            try
            {
                var deadline = DateTime.UtcNow.AddSeconds(30);
                var loaded = false;
                while (DateTime.UtcNow < deadline)
                {
                    if (browser.CoreWebView2 is not null &&
                        await browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('#app')?.hidden === false && !!document.querySelector('figure')") == "true")
                    {
                        loaded = true;
                        break;
                    }
                    await Task.Delay(100);
                }
                if (!loaded) throw new TimeoutException("Original dashboard HTML did not render.");
                var core = browser.CoreWebView2 ?? throw new InvalidOperationException("Browser is unavailable.");
                // Exercise browser layout without changing Windows monitor settings.
                var scale = dpi / 96d;
                browser.ZoomFactor = scale / (form.DeviceDpi / 96d);
                form.MinimumSize = Size.Empty;
                form.ClientSize = new Size((int)Math.Ceiling(402 * scale), (int)Math.Ceiling(414 * scale));
                await Task.Delay(500);
                var layout = await core.ExecuteScriptAsync("JSON.stringify({width:innerWidth,height:innerHeight,scrollWidth:document.documentElement.scrollWidth,scrollHeight:document.documentElement.scrollHeight})");
                using var document = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Deserialize<string>(layout)!);
                var dimensions = document.RootElement;
                if (dimensions.GetProperty("scrollWidth").GetInt32() > dimensions.GetProperty("width").GetInt32() ||
                    dimensions.GetProperty("scrollHeight").GetInt32() > dimensions.GetProperty("height").GetInt32())
                    throw new InvalidOperationException("Dashboard canvas overflows its viewport.");
                await using var image = File.Create(outputPath);
                await core.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, image);
                Console.WriteLine($"Captured original HTML with sanitized runtime fixtures: {outputPath}; browser scale={scale}; physical monitor DPI={form.DeviceDpi}; layout={layout}.");
            }
            catch (Exception error) {failure = error; Console.Error.WriteLine(error);}
            finally {form.Close();}
        };
        Application.Run(form);
        return failure is null ? 0 : 1;
    }
    private static PresenceDashboardSnapshot CreateShowcaseSnapshot(string provider, string connection)
    {
        // Hand-authored presentation fixtures never read user logs, configuration, or account data.
        var now = DateTime.UtcNow;
        var state = provider switch
        {
            ProviderIds.ClaudeCode => "Editing runtime-adapter.js",
            ProviderIds.Antigravity => "Planning a UI update",
            _ => "MCP chrome-devtools"
        };
        var details = provider == ProviderIds.Codex ? "gpt 6.1 sol high" : provider == ProviderIds.ClaudeCode ? "claude sonnet high" : "gemini 3.1 pro high";
        var small = provider == ProviderIds.ClaudeCode ? "https://rpc-art.local/clawd-working-typing.gif" : provider == ProviderIds.Antigravity ? "rpc_antigravity_cli" : "rpc_codex";
        var usage = provider == ProviderIds.Codex ? new TokenUsageSnapshot(null, null, "subsc", new(25, 300, now.AddHours(3)))
            : provider == ProviderIds.Antigravity ? new TokenUsageSnapshot(null, null, PlanName: "Pro", UsageQuotas: [new("model", 0.75m, now.AddHours(2))])
            : new TokenUsageSnapshot(null, null);
        var rendered = new RenderedPresence(details, state, "rpc_reading", small, [], now.AddMinutes(-2),
            CodexActivityKind.AnalyzingProject, RunningCommandKind.Unknown, "");
        return new PresenceDashboardSnapshot(AppProfileKind.Codex, details, "presence-dashboard", rendered, usage,
            connection is "connected" or "unacknowledged" or "disabled", now)
        {
            ProviderId = provider == "none" ? null : provider,
            HasNoActiveProvider = provider == "none",
            IsDiscordConnecting = connection == "connecting",
            PublishedPresence = connection is "unacknowledged" or "disabled" || provider == "none" ? null : new DiscordPresenceSnapshot(
                details, state, provider == ProviderIds.ClaudeCode ? "https://rpc-art.local/clawd-working-typing.gif" : provider == ProviderIds.Antigravity ? "rpc_antigravity_cli" : "rpc_reading", state,
                small, "1 subagent · editing", now.AddMinutes(-2), null, null, [])
        };
    }

}
