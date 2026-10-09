using DiscordRPC;

namespace CodexDiscordPresence.Tests;

public sealed class DiscordRichPresenceBuilderTests
{
    [Theory]
    [InlineData("appsettings.json")]
    [InlineData("appsettings.cli.json")]
    public void Create_SoloAntigravityProfilesOmitSmallImageAndTooltip(string settingsFile)
    {
        using var settings = System.Text.Json.JsonDocument.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, settingsFile)));
        var configured = settings.RootElement.GetProperty("DiscordAntigravity");
        var options = new DiscordOptions
        {
            ClientId = configured.GetProperty("ClientId").GetString()!,
            LargeImageKey = configured.GetProperty("LargeImageKey").GetString(),
            SmallImageKey = configured.GetProperty("SmallImageKey").GetString()
        };
        var rendered = new RenderedPresence("Antigravity", "Working", null, "", [],
            null, CodexActivityKind.AnalyzingProject, RunningCommandKind.Unknown, "")
        {
            ProviderId = ProviderIds.Antigravity
        };

        var payload = DiscordRichPresenceBuilder.Create(options, rendered, "party");

        Assert.Null(payload.Assets.SmallImageKey);
        Assert.Null(payload.Assets.SmallImageText);
        Assert.Equal("rpc_antigravity_cli", payload.Assets.LargeImageKey);
    }

    [Fact]
    public void Create_SoloClaudeCodeOmitsSmallImageWithoutChangingLargeArtwork()
    {
        var rendered = new RenderedPresence("Claude Code", "Working", null, "", [],
            null, CodexActivityKind.AnalyzingProject, RunningCommandKind.Unknown, "")
        {
            ProviderId = ProviderIds.ClaudeCode
        };

        var payload = DiscordRichPresenceBuilder.Create(
            ClaudeCodeAssetPolicy.CreateDiscordOptions(), rendered, "party");

        Assert.Null(payload.Assets.SmallImageKey);
        Assert.Null(payload.Assets.SmallImageText);
        Assert.DoesNotContain("rpc_codex", payload.Assets.LargeImageKey ?? "");
    }

    [Fact]
    public void Create_ConfirmedClaudeSubagentUsesClaudeWorkArtAndObservedStatus()
    {
        var rendered = new RenderedPresence("Claude Code", "Editing", null, "session metadata", [],
            null, CodexActivityKind.ApplyingEdits, RunningCommandKind.Unknown, "")
        {
            ProviderId = ProviderIds.ClaudeCode,
            SubagentActivity = SubagentActivitySummary.Create(1, [SubagentWorkKind.Editing])
        };

        var payload = DiscordRichPresenceBuilder.Create(
            ClaudeCodeAssetPolicy.CreateDiscordOptions(), rendered, "party");

        Assert.Equal("https://cdn.qualit.ly/clawd-working-building.gif", payload.Assets.SmallImageKey);
        Assert.Equal("1 subagent · editing", payload.Assets.SmallImageText);
        Assert.DoesNotContain("rpc_", payload.Assets.SmallImageKey ?? "");
    }

    [Fact]
    public void Create_ConfirmedAntigravitySubagentsUseOnlyAntigravityArtAndGenericActiveText()
    {
        var rendered = new RenderedPresence("Antigravity", "Working", null, "session metadata", [],
            null, CodexActivityKind.AnalyzingProject, RunningCommandKind.Unknown, "")
        {
            ProviderId = ProviderIds.Antigravity,
            PartySize = 3,
            SubagentActivity = SubagentActivitySummary.Create(2, null)
        };

        var payload = DiscordRichPresenceBuilder.Create(
            new DiscordOptions { LargeImageKey = "rpc_antigravity_cli" }, rendered, "party");

        Assert.Equal("rpc_antigravity_cli", payload.Assets.SmallImageKey);
        Assert.Equal("2 subagents active", payload.Assets.SmallImageText);
        Assert.Equal(3, payload.Party!.Size);
    }

    [Fact]
    public void Create_CodexMixedChildWorkUsesGenericArtAndDoesNotInventCoordination()
    {
        var rendered = new RenderedPresence("Codex", "Working", null, "session metadata", [],
            null, CodexActivityKind.AnalyzingProject, RunningCommandKind.Unknown, "")
        {
            ProviderId = ProviderIds.Codex,
            SubagentActivity = SubagentActivitySummary.Create(
                2,
                [SubagentWorkKind.Editing, SubagentWorkKind.Reading])
        };
        var options = new DiscordOptions
        {
            ActivityImageKeys = new(StringComparer.OrdinalIgnoreCase)
            {
                [nameof(CodexActivityKind.ApplyingEdits)] = "codex_editing",
                [nameof(CodexActivityKind.ReadingFiles)] = "codex_reading",
                [nameof(CodexActivityKind.CoordinatingChanges)] = "codex_active"
            }
        };

        var payload = DiscordRichPresenceBuilder.Create(options, rendered, "party");

        Assert.Equal("codex_active", payload.Assets.SmallImageKey);
        Assert.Equal("2 subagents active · 1 editing, 1 reading", payload.Assets.SmallImageText);
        Assert.DoesNotContain("coordinating", payload.Assets.SmallImageText ?? "");
        Assert.DoesNotContain("thinking", payload.Assets.SmallImageText ?? "");
    }

    [Fact]
    public void Create_CodexHomogeneousChildWorkUsesSpecificArtAndOverridesSessionTooltip()
    {
        var rendered = new RenderedPresence("Codex", "Working", null, "session metadata", [],
            null, CodexActivityKind.AnalyzingProject, RunningCommandKind.Unknown, "")
        {
            ProviderId = ProviderIds.Codex,
            SubagentActivity = SubagentActivitySummary.Create(
                2,
                [SubagentWorkKind.Editing, SubagentWorkKind.Editing])
        };
        var options = new DiscordOptions
        {
            SmallImageKey = "rpc_codex",
            ActivityImageKeys = new(StringComparer.OrdinalIgnoreCase)
            {
                [nameof(CodexActivityKind.ApplyingEdits)] = "codex_editing",
                [nameof(CodexActivityKind.CoordinatingChanges)] = "codex_active"
            }
        };

        var payload = DiscordRichPresenceBuilder.Create(options, rendered, "party");

        Assert.Equal("codex_editing", payload.Assets.SmallImageKey);
        Assert.Equal("2 subagents · editing", payload.Assets.SmallImageText);
        Assert.DoesNotContain("session", payload.Assets.SmallImageText ?? "");
    }

    [Theory]
    [InlineData(ProviderIds.Codex)]
    [InlineData(ProviderIds.ClaudeCode)]
    public void Create_ChildActivityArt_UsesCaseInsensitiveMappings(string providerId)
    {
        var rendered = new RenderedPresence("details", "main activity", null, "", [], null,
            CodexActivityKind.ApplyingEdits, RunningCommandKind.Unknown, "")
        {
            ProviderId = providerId,
            SubagentActivity = SubagentActivitySummary.Create(1, [SubagentWorkKind.Editing])
        };
        var options = new DiscordOptions
        {
            ActivityImageKeys = new(StringComparer.Ordinal) { ["applyingedits"] = "child_editing" }
        };

        Assert.Equal("child_editing", DiscordRichPresenceBuilder.Create(options, rendered, "party").Assets.SmallImageKey);
    }

    [Theory]
    [InlineData(ProviderIds.Codex)]
    [InlineData(ProviderIds.ClaudeCode)]
    public void Create_MissingChildActivityArt_OmitsSmallImageRatherThanFailing(string providerId)
    {
        var rendered = new RenderedPresence("details", "main activity", null, "", [], null,
            CodexActivityKind.ApplyingEdits, RunningCommandKind.Unknown, "")
        {
            ProviderId = providerId,
            SubagentActivity = SubagentActivitySummary.Create(1, [SubagentWorkKind.Editing])
        };

        var payload = DiscordRichPresenceBuilder.Create(new DiscordOptions { ActivityImageKeys = null! }, rendered, "party");
        Assert.Null(payload.Assets.SmallImageKey);
        Assert.Null(payload.Assets.SmallImageText);
    }

    [Fact]
    public void Create_UnknownProviderDoesNotInheritAnotherProvidersChildArt()
    {
        var rendered = new RenderedPresence("Unknown", "Working", null, "session metadata", [],
            null, CodexActivityKind.AnalyzingProject, RunningCommandKind.Unknown, "")
        {
            ProviderId = "future-provider",
            SubagentActivity = SubagentActivitySummary.Create(1, [SubagentWorkKind.Editing])
        };

        var payload = DiscordRichPresenceBuilder.Create(new DiscordOptions(), rendered, "party");

        Assert.Null(payload.Assets.SmallImageKey);
        Assert.Null(payload.Assets.SmallImageText);
    }

    [Fact]
    public void Create_ProducesThePayloadAndSnapshotUsedForTheDiscordPreview()
    {
        var startedAt = new DateTime(2026, 9, 7, 3, 4, 5, DateTimeKind.Utc);
        var rendered = new RenderedPresence(
            "details",
            "state",
            "large text",
            "small text",
            [
                new RenderedButton("one", "https://example.com/one"),
                new RenderedButton("two", "https://example.com/two"),
                new RenderedButton("three", "https://example.com/three")
            ],
            startedAt,
            CodexActivityKind.ApplyingEdits,
            RunningCommandKind.Unknown,
            "")
        {
            PartySize = 3
        };

        var options = new DiscordOptions();
        var payload = DiscordRichPresenceBuilder.Create(options, rendered, "party-id");
        var snapshot = DiscordPresenceSnapshot.From(payload);

        Assert.Equal(rendered.Details, payload.Details);
        Assert.Equal(rendered.State, payload.State);
        Assert.Equal(
            DiscordAssetKeyResolver.ResolveLargeImageReference(options, rendered),
            payload.Assets!.LargeImageKey);
        Assert.Equal(rendered.LargeImageText, payload.Assets.LargeImageText);
        Assert.Null(payload.Assets.SmallImageText);
        Assert.Equal(startedAt, payload.Timestamps!.Start);
        Assert.Equal(3, payload.Party!.Size);
        Assert.Equal(3, payload.Party.Max);
        Assert.Equal(2, payload.Buttons!.Length);

        Assert.Equal(rendered.Details, snapshot.Details);
        Assert.Equal(rendered.State, snapshot.State);
        Assert.Equal(payload.Type, snapshot.ActivityType);
        Assert.Equal(payload.Assets.LargeImageKey, snapshot.LargeImageKey);
        Assert.Equal(payload.Assets.SmallImageKey, snapshot.SmallImageKey);
        Assert.Equal(startedAt, snapshot.StartedAtUtc);
        Assert.Equal(3, snapshot.PartySize);
        Assert.Equal(3, snapshot.PartyMax);
        Assert.Equal(["one", "two"], snapshot.Buttons.Select(button => button.Label).ToArray());
    }

    [Fact]
    public void Create_AntigravityOmitsPartyForSoloPresence()
    {
        var rendered = new RenderedPresence(
            "details",
            "working",
            null,
            "small text",
            [],
            DateTime.UtcNow,
            CodexActivityKind.AnalyzingProject,
            RunningCommandKind.Unknown,
            "")
        {
            ProviderId = ProviderIds.Antigravity,
            PartySize = 1
        };

        var options = new DiscordOptions
        {
            ClientId = "1548038167041671259",
            LargeImageKey = "rpc_antigravity_cli",
            SmallImageKey = null,
            ExternalImageUrls = new(StringComparer.OrdinalIgnoreCase)
        };

        var payload = DiscordRichPresenceBuilder.Create(options, rendered, "antigravity-party");

        Assert.Null(payload.Party);
        Assert.Equal("rpc_antigravity_cli", payload.Assets!.LargeImageKey);
        Assert.Null(payload.Assets.SmallImageKey);
    }

    [Fact]
    public void Create_AntigravityPublishesPartyForExplicitActiveSubagents()
    {
        var rendered = new RenderedPresence(
            "details",
            "working",
            null,
            "small text",
            [],
            DateTime.UtcNow,
            CodexActivityKind.AnalyzingProject,
            RunningCommandKind.Unknown,
            "")
        {
            ProviderId = ProviderIds.Antigravity,
            PartySize = 3
        };

        var payload = DiscordRichPresenceBuilder.Create(
            new DiscordOptions { ClientId = "1548038167041671259" },
            rendered,
            "antigravity-party");

        Assert.Equal(3, payload.Party!.Size);
        Assert.Equal(3, payload.Party.Max);
    }

    [Fact]
    public void Create_ProviderSwitchKeepsTheSharedRuntimeTimestamp()
    {
        var startedAt = new DateTime(2026, 9, 12, 3, 0, 0, DateTimeKind.Utc);
        var codexPresence = new RenderedPresence(
            "codex details",
            "Working",
            null,
            "",
            [],
            startedAt,
            CodexActivityKind.AnalyzingProject,
            RunningCommandKind.Unknown,
            "")
        {
            ProviderId = ProviderIds.Codex
        };
        var antigravityPresence = codexPresence with
        {
            Details = "antigravity details",
            ProviderId = ProviderIds.Antigravity
        };

        var codexPayload = DiscordRichPresenceBuilder.Create(
            new DiscordOptions(),
            codexPresence,
            "codex-party");
        var antigravityPayload = DiscordRichPresenceBuilder.Create(
            new DiscordOptions { ClientId = "1548038167041671259" },
            antigravityPresence,
            "antigravity-party");

        Assert.Equal(codexPayload.Timestamps!.Start, antigravityPayload.Timestamps!.Start);
    }
}
