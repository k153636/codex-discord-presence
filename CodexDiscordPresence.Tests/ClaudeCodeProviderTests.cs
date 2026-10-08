using System.Text.Json;
using System.Text.Json.Nodes;

namespace CodexDiscordPresence.Tests;

public sealed class ClaudeCodeProviderTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static readonly string ProjectPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "claude-project"));

    [Fact]
    public void Parse_RealHookShape_RetainsOnlySafeObservationFields()
    {
        var json = JsonSerializer.Serialize(new
        {
            session_id = "main", cwd = ProjectPath, hook_event_name = "PreToolUse",
            tool_name = "Edit", tool_use_id = "edit-1", prompt = "private prompt",
            tool_input = new { file_path = Path.Combine(ProjectPath, "secret", "app.cs"), old_string = "private source" },
            tool_response = "private output"
        });
        var parsed = ClaudeCodeHookParser.Parse(json, Now);
        Assert.NotNull(parsed);
        Assert.Equal("app.cs", parsed.FileName);
        var persisted = JsonSerializer.Serialize(parsed);
        Assert.DoesNotContain("private", persisted);
        Assert.DoesNotContain("secret", persisted);
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("[]")]
    [InlineData("{\"session_id\":\"s\",\"cwd\":\"relative\",\"hook_event_name\":\"Stop\"}")]
    [InlineData("{\"session_id\":\"s\",\"cwd\":\"C:/repo\",\"hook_event_name\":\"Unknown\"}")]
    public void Parse_InvalidOrUnknownPayload_FailsClosed(string json) => Assert.Null(ClaudeCodeHookParser.Parse(json, Now));

    [Fact]
    public void Parse_SubagentToolEvent_DoesNotReplaceMainActivity()
    {
        var json = JsonSerializer.Serialize(new
        {
            session_id = "main", cwd = ProjectPath, hook_event_name = "PreToolUse",
            tool_name = "Bash", agent_id = "child"
        });
        Assert.Null(ClaudeCodeHookParser.Parse(json, Now));
    }

    [Fact]
    public void Apply_ExplicitAgents_DeduplicatesAndRemovesWithoutChangingMainTool()
    {
        var state = Apply(null, "PreToolUse", "Edit", "edit-1", "app.cs");
        state = Apply(state, "SubagentStart", agentId: "child");
        state = Apply(state, "SubagentStart", agentId: "child");
        Assert.Equal(CodexActivityKind.ApplyingEdits, ClaudeCodePresenceProjection.Build(state).ActivityKind);
        Assert.Equal(2, ClaudeCodePresenceProjection.Build(state).PartySize);
        state = Apply(state, "SubagentStop", agentId: "child");
        Assert.Null(ClaudeCodePresenceProjection.Build(state).PartySize);
        Assert.Single(state.Tools);
    }

    [Fact]
    public void Apply_CompletedMcp_RemovesOnlyCompletedServerAndNextEventWins()
    {
        var state = Apply(null, "PreToolUse", "mcp__chrome-devtools__navigate", "1");
        state = Apply(state, "PreToolUse", "mcp__blender__inspect", "2");
        var presence = Render(state);
        Assert.Equal("MCP chrome-devtools＆+1", presence.State);
        Assert.False(presence.IsThinking);
        Assert.Equal("claude_working", DiscordAssetKeyResolver.ResolveLargeImageKey(ClaudeCodeAssetPolicy.CreateDiscordOptions(), presence));
        state = Apply(state, "PostToolUse", "mcp__chrome-devtools__navigate", "1");
        Assert.Equal("MCP blender", Render(state).State);
        state = Apply(state, "PostToolUse", "mcp__blender__inspect", "2");
        Assert.Equal("Thinking", Render(state).State);
        Assert.False(ClaudeCodePresenceProjection.Build(state).IsMcpOperation);
    }

    [Fact]
    public void Apply_FourActiveFiles_CountExcludesRepresentativeAndCompletedFiles()
    {
        ClaudeCodeSessionObservation? state = null;
        for (var i = 0; i < 4; i++)
        {
            state = Apply(state, "PreToolUse", "Edit", i.ToString(), $"file{i}.cs");
        }
        Assert.Equal("Editing file0.cs + 3 files", Render(state!).State);
        state = Apply(state, "PostToolUse", "Edit", "0");
        Assert.Equal("Editing file1.cs", Render(state).State);
    }

    [Theory]
    [InlineData("Read", CodexActivityKind.ReadingFiles, "Reading app.cs")]
    [InlineData("WebSearch", CodexActivityKind.Researching, "Researching")]
    [InlineData("Bash", CodexActivityKind.RunningCommand, "Run Command")]
    public void Build_ToolActivity_UsesSpecificCommonVocabulary(string tool, CodexActivityKind kind, string label)
    {
        var state = Apply(null, "PreToolUse", tool, "1", "app.cs");
        Assert.Equal(kind, ClaudeCodePresenceProjection.Build(state).ActivityKind);
        Assert.Equal(label, Render(state).State);
    }

    [Fact]
    public void Apply_WaitStopEndAndRestart_ClearOldOperationsAndAgents()
    {
        var state = Apply(null, "PreToolUse", "Edit", "1", "app.cs");
        state = Apply(state, "SubagentStart", agentId: "agent");
        state = Apply(state, "PermissionRequest");
        Assert.Equal(CodexActivityKind.WaitingForInput, ClaudeCodePresenceProjection.Build(state).ActivityKind);
        Assert.Empty(ClaudeCodePresenceProjection.Build(state).ActivityFilePaths);
        state = Apply(state, "Stop");
        Assert.Empty(state.Tools);
        state = Apply(state, "SessionEnd");
        Assert.True(state.Ended);
        Assert.Empty(state.ActiveAgentIds);
        state = Apply(state, "SessionStart");
        Assert.False(state.Ended);
        Assert.Empty(state.Tools);
    }

    [Fact]
    public void Store_FreshnessProjectAndEndedBoundaries_AreEnforced()
    {
        var state = Apply(null, "UserPromptSubmit");
        Assert.True(ClaudeCodeObservationStore.IsEligible(state, ProjectPath, Now, TimeSpan.FromMinutes(5)));
        Assert.False(ClaudeCodeObservationStore.IsEligible(state, ProjectPath, Now.AddMinutes(6), TimeSpan.FromMinutes(5)));
        Assert.False(ClaudeCodeObservationStore.IsEligible(state, ProjectPath, Now.AddSeconds(-1), TimeSpan.FromMinutes(5)));
        Assert.False(ClaudeCodeObservationStore.IsEligible(state, ProjectPath + "-other", Now, TimeSpan.FromMinutes(5)));
        Assert.False(ClaudeCodeObservationStore.IsEligible(state with { Ended = true }, ProjectPath, Now, TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void Apply_LateAndPostEndEvents_CannotResurrectOldSession()
    {
        var state = Apply(null, "UserPromptSubmit");
        var older = new ClaudeCodeHookEvent("main", ProjectPath, "PreToolUse", Now.AddSeconds(-1), "Edit", "old", "old.cs");
        Assert.Same(state, ClaudeCodeSessionObservation.Apply(state, older));
        state = Apply(state, "SessionEnd");
        Assert.Same(state, Apply(state, "PreToolUse", "Edit", "late", "late.cs"));
        Assert.False(Apply(state, "SessionStart").Ended);
    }

    [Fact]
    public void Context_ClaudeHasNoCodexBillingQuotaPartyOrModelFallback()
    {
        var state = Apply(null, "UserPromptSubmit");
        var context = Context(state);
        Assert.Equal(ProviderIds.ClaudeCode, context.ProviderId);
        Assert.Equal("Claude Code", context.ModelName);
        Assert.Null(context.TokenUsage.BillingType);
        Assert.Null(context.TokenUsage.EstimatedCostUsd);
        Assert.Null(context.TokenUsage.RateLimit);
        Assert.Null(context.TokenUsage.UsageQuotas);
        Assert.Null(context.Activity.PartySize);
        var rendered = new PresenceTemplateRenderer().Render(new PresenceTemplateOptions
        {
            Details = "{ModelName} {CodexStatus} {CodexProcessName} {Cost} {BillingType}{RateLimitDetails}",
            State = "{ActivityLine}"
        }, context);
        Assert.Equal("Claude Code", rendered.Details);
        var options = ClaudeCodeAssetPolicy.CreateDiscordOptions();
        Assert.Equal("claude_notification", options.SmallImageKey);
        Assert.DoesNotContain(options.ExternalImageUrls.Keys, key => key.StartsWith("rpc_"));
        Assert.Equal("https://cdn.qualit.ly/clawd-working-typing.gif", DiscordAssetKeyResolver.ResolveLargeImageReference(options, rendered));
    }

    [Fact]
    public void ProviderSwitch_NewClaudeActivityWinsAndOldIdleDoesNotTakeOver()
    {
        var gate = new ProviderActivationGate(ProviderIds.Codex);
        var codex = new ProviderSelectionCandidate(ProviderIds.Codex, true, true, Now, true, true,
            IsActive: true, ActivityStartedAtUtc: Now);
        var claude = codex with { ProviderId = ProviderIds.ClaudeCode, LastObservedAtUtc = Now.AddSeconds(1), ActivityStartedAtUtc = Now.AddSeconds(1) };
        Assert.Equal(ProviderIds.Codex, gate.Select([codex], Now)!.ProviderId);
        Assert.Equal(ProviderIds.Codex, gate.Select([codex, claude], Now.AddSeconds(1))!.ProviderId);
        Assert.Equal(ProviderIds.ClaudeCode, gate.Select([codex, claude], Now.AddSeconds(5))!.ProviderId);
        var idle = claude with { IsActive = false, LastObservedAtUtc = Now.AddSeconds(-10) };
        gate.Reset(ProviderIds.Codex);
        Assert.Equal(ProviderIds.Codex, gate.Select([codex, idle], Now)!.ProviderId);
        Assert.Equal(ProviderIds.Codex, gate.Select([codex, claude with { IsAvailable = false }], Now)!.ProviderId);
    }

    [Fact]
    public void Installer_RoundTripPreservesOtherHooksAndSettings_AndIsIdempotent()
    {
        InTemporaryDirectory(directory =>
        {
            var settings = Path.Combine(directory, "settings.json");
            var original = JsonNode.Parse("{\"model\":\"user-model\",\"statusLine\":{\"command\":\"user-status\"},\"hooks\":{\"Stop\":[{\"hooks\":[{\"type\":\"command\",\"command\":\"user-hook\"}]}]}}")!;
            File.WriteAllText(settings, original.ToJsonString());
            var installer = new ClaudeCodeHookInstaller(settings, Path.Combine(directory, "owned"), "C:/tool/rpc.exe");
            installer.Install();
            var installed = File.ReadAllText(settings);
            installer.Install();
            Assert.Equal(installed, File.ReadAllText(settings));
            Assert.Contains("user-hook", installed);
            Assert.Contains("user-status", installed);
            installer.Uninstall();
            Assert.True(JsonNode.DeepEquals(original, JsonNode.Parse(File.ReadAllText(settings))));
            Assert.NotEmpty(Directory.EnumerateFiles(Path.Combine(directory, "owned"), "settings-backup-*.json"));
        });
    }

    [Fact]
    public void Installer_ModifiedOwnedHookOrMissingOwnership_FailsWithoutOverwriting()
    {
        InTemporaryDirectory(directory =>
        {
            var settings = Path.Combine(directory, "settings.json");
            var owned = Path.Combine(directory, "owned");
            var installer = new ClaudeCodeHookInstaller(settings, owned, "C:/tool/rpc.exe");
            installer.Install();
            var json = JsonNode.Parse(File.ReadAllText(settings))!;
            json["hooks"]!["Stop"]![0]!["hooks"]![0]!["timeout"] = 10;
            File.WriteAllText(settings, json.ToJsonString());
            var changed = File.ReadAllText(settings);
            Assert.Throws<InvalidOperationException>(() => installer.Uninstall());
            Assert.Equal(changed, File.ReadAllText(settings));
            File.Delete(Path.Combine(owned, "hook-owner.json"));
            Assert.Throws<InvalidOperationException>(() => installer.Install());
            Assert.Equal(changed, File.ReadAllText(settings));
        });
    }

    [Fact]
    public void Store_RoundTripKeepsSessionsIsolated_AndSkipsMalformedFiles()
    {
        InTemporaryDirectory(directory =>
        {
            var store = new ClaudeCodeObservationStore(directory);
            store.Write(new("one", ProjectPath, "PreToolUse", Now, "Edit", "1", "one.cs"));
            store.Write(new("two", ProjectPath + "-other", "UserPromptSubmit", Now.AddSeconds(1)));
            File.WriteAllText(Path.Combine(directory, "malformed.json"), "[]");
            Assert.Equal("one", store.Select(ProjectPath, Now.AddSeconds(2), TimeSpan.FromMinutes(5))!.SessionId);
            store.Write(new("one", ProjectPath, "SessionEnd", Now.AddSeconds(3)));
            Assert.Equal("two", store.Select(ProjectPath, Now.AddSeconds(4), TimeSpan.FromMinutes(5))!.SessionId);
        });
    }

    [Fact]
    public void Transcript_UsesMainSessionMetadataOnly_AndIgnoresIncompleteLine()
    {
        InTemporaryDirectory(directory =>
        {
            var path = Path.Combine(directory, "main.jsonl");
            File.WriteAllLines(path,
            [
                "{\"type\":\"assistant\",\"sessionId\":\"main\",\"effort\":\"high\",\"message\":{\"model\":\"claude-opus-4-6\",\"content\":[{\"type\":\"thinking\",\"thinking\":\"private\"}]} }",
                "{\"type\":\"assistant\",\"sessionId\":\"other\",\"message\":{\"model\":\"wrong\"}}",
                "{\"type\":\"assistant\",\"sessionId\":\"main\",\"isSidechain\":true,\"message\":{\"model\":\"child\"}}",
                "{incomplete"
            ]);
            var metadata = ClaudeCodeTranscriptMetadata.Read(path, "main");
            Assert.Equal("claude-opus-4-6", metadata.Model);
            Assert.Equal("high", metadata.Effort);
            var state = Apply(null, "UserPromptSubmit") with { SessionId = "main", TranscriptPath = path };
            Assert.Equal("claude opus 4.6", Context(state).ModelName);
            Assert.Null(Context(state).Activity.LatestThinkingSummary);
        });
    }

    [Fact]
    public void Configuration_PartialClaudeOverrides_KeepClaudeAssetsAndDisableByDefault()
    {
        InTemporaryDirectory(directory =>
        {
            var path = Path.Combine(directory, "settings.json");
            File.WriteAllText(path, "{\"DiscordClaudeCode\":{\"ClientId\":\"123\"}}");
            foreach (var options in new[] { AppOptions.LoadMerged(path), AppOptions.LoadFromFile(path) })
            {
                Assert.Equal("123", options.DiscordClaudeCode.ClientId);
                Assert.Equal("claude_notification", options.DiscordClaudeCode.SmallImageKey);
                Assert.All(options.DiscordClaudeCode.ActivityImageKeys.Values, key => Assert.StartsWith("claude_", key));
                Assert.False(options.Providers[ProviderIds.ClaudeCode].Enabled);
            }
        });
    }

    [Fact]
    public void TranscriptBootstrap_StructuredToolsAndEndTurn_IgnoreUnrelatedTimestampAndSidechain()
    {
        InTemporaryDirectory(directory =>
        {
            var path = Path.Combine(directory, "main.jsonl");
            var prefix = new { sessionId = "main", cwd = ProjectPath, timestamp = Now };
            var lines = new[]
            {
                JsonSerializer.Serialize(new { prefix.sessionId, prefix.cwd, prefix.timestamp, type = "assistant",
                    message = new { model = "claude-opus-4-6", content = new[] { new { type = "tool_use", id = "t", name = "Edit", input = new { file_path = Path.Combine(ProjectPath, "app.cs") } } } } }),
                JsonSerializer.Serialize(new { prefix.sessionId, prefix.cwd, timestamp = Now.AddSeconds(1), type = "system", subtype = "unrelated" }),
                JsonSerializer.Serialize(new { prefix.sessionId, prefix.cwd, timestamp = Now.AddSeconds(2), type = "assistant", isSidechain = true,
                    message = new { content = new[] { new { type = "text", text = "child-private" } } } })
            };
            File.WriteAllLines(path, lines);
            var observation = ClaudeCodeTranscriptActivityReader.Read(path)!;
            Assert.Equal(Now, observation.ObservedAtUtc);
            Assert.Equal("Editing app.cs", Render(observation).State);
            File.AppendAllText(path, JsonSerializer.Serialize(new { prefix.sessionId, prefix.cwd,
                timestamp = Now.AddSeconds(3), type = "assistant", message = new { stop_reason = "end_turn",
                    content = new[] { new { type = "text", text = "private final response" } } } }) + "\n");
            observation = ClaudeCodeTranscriptActivityReader.Read(path)!;
            Assert.Equal("Stop", observation.EventName);
            Assert.Empty(observation.Tools);
            Assert.DoesNotContain("private", JsonSerializer.Serialize(observation));
        });
    }

    [Fact]
    public void Store_EndedSessionTombstone_PreventsTranscriptResurrection()
    {
        InTemporaryDirectory(directory =>
        {
            var store = new ClaudeCodeObservationStore(directory);
            store.Write(new("main", ProjectPath, "SessionEnd", Now));
            Assert.Null(store.Select(ProjectPath, Now, TimeSpan.FromMinutes(5)));
            Assert.True(store.HasSessionEvidence("main"));
            Assert.False(store.HasSessionEvidence("different"));
        });
    }

    [Fact]
    public void TranscriptBootstrap_LargeIrrelevantEvent_DoesNotHideRecentActivityOrModel()
    {
        InTemporaryDirectory(directory =>
        {
            var path = Path.Combine(directory, "main.jsonl");
            var header = JsonSerializer.Serialize(new { type = "assistant", sessionId = "main", cwd = ProjectPath, timestamp = Now,
                message = new { model = "claude-opus-5-5", content = new[] { new { type = "thinking", thinking = "private" } } } });
            var largeLine = JsonSerializer.Serialize(new { type = "attachment", text = new string('x', 700_000) });
            File.WriteAllLines(path, [header, largeLine]);
            Assert.Equal(Now, ClaudeCodeTranscriptActivityReader.Read(path)!.ObservedAtUtc);
            Assert.Equal("claude-opus-5-5", ClaudeCodeTranscriptMetadata.Read(path, "main").Model);
            Assert.Equal("claude opus 5.5", ClaudeCodePresenceProjection.NormalizeModel("claude-opus-5-5"));
        });
    }

    [Theory]
    [InlineData(360)]
    [InlineData(432)]
    [InlineData(600)]
    public void Dashboard_ProviderControlsFitAtSupportedWidths_AndClaudePersists(int width)
    {
        Exception? failure = null;
        InTemporaryDirectory(directory =>
        {
            var state = new PresenceRuntimeState();
            var statePath = Path.Combine(directory, "state.json");
            var thread = new Thread(() =>
            {
                try
                {
                    using var form = new System.Windows.Forms.Form { ClientSize = new System.Drawing.Size(width, 160) };
                    using var panel = new ProviderIntegrationPanel { Dock = System.Windows.Forms.DockStyle.Top, Height = ProviderIntegrationPanel.PreferredHeight };
                    form.Controls.Add(panel);
                    panel.ProviderEnabledChanged += (_, change) =>
                    {
                        state.SetProviderEnabled(change.ProviderId, change.Enabled);
                        new PresenceStateStore().Save(statePath, state);
                    };
                    form.Show();
                    System.Windows.Forms.Application.DoEvents();
                    foreach (var checkBox in new[] { panel.CodexCheckBox, panel.AntigravityCheckBox, panel.ClaudeCodeCheckBox })
                    {
                        Assert.True(checkBox.Parent!.ClientRectangle.Contains(checkBox.Bounds), $"{checkBox.Text}: {checkBox.Bounds} in {checkBox.Parent.ClientRectangle}");
                    }
                    panel.ClaudeCodeCheckBox.Checked = true;
                    form.Close();
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            Assert.Null(failure);
            Assert.True(new PresenceStateStore().Load(statePath).IsProviderEnabled(ProviderIds.ClaudeCode, false));
        });
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("\"text\"")]
    public void Transcript_NonObjectJsonLine_DoesNotBreakObservation(string malformedLine)
    {
        InTemporaryDirectory(directory =>
        {
            var path = Path.Combine(directory, "main.jsonl");
            var valid = JsonSerializer.Serialize(new { type = "user", sessionId = "main", cwd = ProjectPath,
                timestamp = Now, message = new { content = "private" } });
            File.WriteAllLines(path, [valid, malformedLine]);
            Assert.Equal("UserPromptSubmit", ClaudeCodeTranscriptActivityReader.Read(path)!.EventName);
        });
    }

    [Fact]
    public void Transcript_NonObjectMessage_DoesNotBreakActivityOrEraseMetadata()
    {
        InTemporaryDirectory(directory =>
        {
            var path = Path.Combine(directory, "main.jsonl");
            var valid = JsonSerializer.Serialize(new { type = "assistant", sessionId = "main", cwd = ProjectPath,
                timestamp = Now, effort = "high", message = new { model = "claude-opus-4-6",
                    content = new[] { new { type = "thinking", thinking = "private" } } } });
            var invalid = JsonSerializer.Serialize(new { type = "assistant", sessionId = "main", cwd = ProjectPath,
                timestamp = Now.AddSeconds(1), message = (object?)null });
            File.WriteAllLines(path, [valid, invalid]);
            Assert.Equal(Now, ClaudeCodeTranscriptActivityReader.Read(path)!.ObservedAtUtc);
            Assert.Equal("high", ClaudeCodeTranscriptMetadata.Read(path, "main").Effort);
        });
    }

    [Theory]
    [InlineData("Tools", "[null]")]
    [InlineData("Tools", "[{\"Id\":\"id\",\"Name\":null}]")]
    [InlineData("Tools", "[{\"Id\":\"id\",\"Name\":\"Edit\",\"FileName\":\"../private.cs\"}]")]
    [InlineData("ActiveAgentIds", "[null]")]
    [InlineData("EventName", "\"Unknown\"")]
    public void Store_InvalidPersistedObservation_IsRejected(string field, string invalidValue)
    {
        InTemporaryDirectory(directory =>
        {
            var store = new ClaudeCodeObservationStore(directory);
            var json = JsonSerializer.SerializeToNode(Apply(null, "UserPromptSubmit"))!;
            json[field] = JsonNode.Parse(invalidValue);
            File.WriteAllText(Path.Combine(directory, "invalid.json"), json.ToJsonString());
            Assert.Null(store.Select(ProjectPath, Now, TimeSpan.FromMinutes(5)));
        });
    }

    [Fact]
    public void Installer_FailedExecutableUpgrade_PreservesOwnershipForUninstall()
    {
        InTemporaryDirectory(directory =>
        {
            var settings = Path.Combine(directory, "settings.json");
            var owned = Path.Combine(directory, "owned");
            var original = new ClaudeCodeHookInstaller(settings, owned, "C:/old/rpc.exe");
            original.Install();
            var before = File.ReadAllText(settings);
            var upgrade = new ClaudeCodeHookInstaller(settings, owned, "C:/new/rpc.exe");
            using (var locked = new FileStream(settings, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var failure = Record.Exception(() => upgrade.Install());
                Assert.True(failure is IOException or UnauthorizedAccessException);
                failure = Record.Exception(() => upgrade.Install());
                Assert.True(failure is IOException or UnauthorizedAccessException);
            }
            Assert.Equal(before, File.ReadAllText(settings));
            upgrade.Uninstall();
            Assert.Null(JsonNode.Parse(File.ReadAllText(settings))!["hooks"]);
        });
    }

    [Fact]
    public void Observation_SubagentLifecycle_DoesNotAdvanceMainActivityEvidence()
    {
        var main = Apply(null, "UserPromptSubmit");
        var party = ClaudeCodeSessionObservation.Apply(main,
            new("main", ProjectPath, "SubagentStart", Now.AddSeconds(10), AgentId: "worker"));
        Assert.Equal(Now, party.LastActivityEventAtUtc);
        Assert.Equal(Now.AddSeconds(10), party.ObservedAtUtc);
        Assert.Single(party.ActiveAgentIds);
        var next = ClaudeCodeSessionObservation.Apply(party,
            new("main", ProjectPath, "PreToolUse", Now.AddSeconds(20), ToolName: "Read"));
        Assert.Equal(Now.AddSeconds(20), next.LastActivityEventAtUtc);
    }

    private static ClaudeCodeSessionObservation Apply(ClaudeCodeSessionObservation? previous, string name,
        string? tool = null, string? toolId = null, string? file = null, string? agentId = null) =>
        ClaudeCodeSessionObservation.Apply(previous, new("main", ProjectPath, name, Now, tool, toolId, file, agentId));

    private static PresenceContext Context(ClaudeCodeSessionObservation state) => ClaudeCodePresenceProjection.CreateContext(
        state, new ProjectSnapshot("project", ProjectPath, "unrelated.cs", "unrelated.cs", 1, 1, 1, []),
        new GitSnapshot(true, 7, null), new SessionSnapshot(Now.UtcDateTime, TimeSpan.Zero));

    private static RenderedPresence Render(ClaudeCodeSessionObservation state) =>
        new PresenceTemplateRenderer().Render(new PresenceTemplateOptions { Details = "{ModelName}", State = "{ActivityLine}" }, Context(state));

    private static void InTemporaryDirectory(Action<string> action)
    {
        var directory = Path.Combine(Path.GetTempPath(), "claude-rpc-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { action(directory); }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
