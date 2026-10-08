using CodexDiscordPresence;

namespace CodexDiscordPresence.Tests;

public sealed class DiscordAssetKeyResolverTests
{
    [Fact]
    public void ResolveLargeImageKey_UsesProviderAssetWithoutChangingCodexStateMapping()
    {
        var key = DiscordAssetKeyResolver.ResolveLargeImageKey(
            new DiscordOptions(),
            CreatePresence(CodexActivityKind.ApplyingEdits) with
            {
                ProviderId = ProviderIds.Antigravity
            });

        Assert.Equal("rpc_antigravity_cli", key);
    }

    [Fact]
    public void ResolveLargeImageReference_UsesConfiguredAntigravityIconFallback()
    {
        const string iconUrl = "https://antigravity.google/assets/image/brand/antigravity-icon__full-color.png";
        var options = new DiscordOptions
        {
            ExternalImageUrls = new(StringComparer.OrdinalIgnoreCase)
            {
                ["rpc_antigravity_cli"] = iconUrl
            }
        };
        var presence = new RenderedPresence(
            "details",
            "state",
            null,
            "small",
            [],
            null,
            CodexActivityKind.Ready,
            RunningCommandKind.Unknown,
            "")
        {
            ProviderId = ProviderIds.Antigravity
        };

        Assert.Equal(iconUrl, DiscordAssetKeyResolver.ResolveLargeImageReference(options, presence));
    }

    [Theory]
    [InlineData("antigravity")]
    [InlineData("ANTIGRAVITY")]
    [InlineData("  Antigravity  ")]
    public void ResolveProviderLargeImageKey_UsesFixedAntigravityCliKey(string providerId)
    {
        var key = DiscordAssetKeyResolver.ResolveProviderLargeImageKey(providerId);

        Assert.Equal("rpc_antigravity_cli", key);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("google")]
    [InlineData("antigravity-extra")]
    public void ResolveProviderLargeImageKey_ReturnsNullForMissingOrUnknownProvider(string? providerId)
    {
        var key = DiscordAssetKeyResolver.ResolveProviderLargeImageKey(providerId);

        Assert.Null(key);
    }

    [Theory]
    [InlineData(CodexActivityKind.Offline, "rpc_sleeping")]
    [InlineData(CodexActivityKind.Ready, "rpc_sleeping")]
    [InlineData(CodexActivityKind.AnalyzingProject, "rpc_thinking")]
    [InlineData(CodexActivityKind.Planning, "rpc_thinking")]
    [InlineData(CodexActivityKind.ApplyingEdits, "rpc_coding")]
    [InlineData(CodexActivityKind.CoordinatingChanges, "rpc_coding")]
    [InlineData(CodexActivityKind.CreatingFiles, "rpc_coding")]
    [InlineData(CodexActivityKind.DeletingFiles, "rpc_coding")]
    [InlineData(CodexActivityKind.Refactoring, "rpc_coding")]
    [InlineData(CodexActivityKind.ReadingFiles, "rpc_reading")]
    [InlineData(CodexActivityKind.Researching, "rpc_searching")]
    [InlineData(CodexActivityKind.WaitingForInput, "rpc_sleeping")]
    [InlineData(CodexActivityKind.Stalled, "rpc_sleeping")]
    public void ResolveLargeImageKey_UsesActivityMapping(CodexActivityKind activityKind, string expectedKey)
    {
        var presence = CreatePresence(activityKind);

        var key = DiscordAssetKeyResolver.ResolveLargeImageKey(new DiscordOptions(), presence);

        Assert.Equal(expectedKey, key);
    }

    [Theory]
    [InlineData(RunningCommandKind.Unknown, "rpc_coding")]
    [InlineData(RunningCommandKind.Git, "rpc_reading")]
    [InlineData(RunningCommandKind.Search, "rpc_searching")]
    [InlineData(RunningCommandKind.Build, "rpc_coding")]
    [InlineData(RunningCommandKind.Test, "rpc_debugging")]
    public void ResolveLargeImageKey_UsesCommandMapping(RunningCommandKind commandKind, string expectedKey)
    {
        var presence = CreatePresence(CodexActivityKind.RunningCommand, commandKind);

        var key = DiscordAssetKeyResolver.ResolveLargeImageKey(new DiscordOptions(), presence);

        Assert.Equal(expectedKey, key);
    }

    [Fact]
    public void ResolveLargeImageKey_PrefersCommandMappingOverGenericRunningCommandMapping()
    {
        var options = new DiscordOptions
        {
            ActivityImageKeys = new(StringComparer.OrdinalIgnoreCase)
            {
                [nameof(CodexActivityKind.RunningCommand)] = "generic"
            },
            RunningCommandImageKeys = new(StringComparer.OrdinalIgnoreCase)
            {
                [nameof(RunningCommandKind.Search)] = "specific"
            }
        };

        var key = DiscordAssetKeyResolver.ResolveLargeImageKey(
            options,
            CreatePresence(CodexActivityKind.RunningCommand, RunningCommandKind.Search));

        Assert.Equal("specific", key);
    }

    [Fact]
    public void ResolveLargeImageKey_UsesSuccessAssetForSuccessfulCompletion()
    {
        var key = DiscordAssetKeyResolver.ResolveLargeImageKey(
            new DiscordOptions(),
            CreatePresence(CodexActivityKind.Ready) with
            {
                IsSuccessfulCompletion = true,
                WaitingStartedAt = DateTime.UtcNow.AddSeconds(-30)
            });

        Assert.Equal("rpc_success", key);
    }

    [Fact]
    public void ResolveLargeImageKey_UsesErrorAssetForExplicitFailure()
    {
        var key = DiscordAssetKeyResolver.ResolveLargeImageKey(
            new DiscordOptions(),
            CreatePresence(CodexActivityKind.Ready) with { IsError = true });

        Assert.Equal("rpc_error", key);
    }

    [Fact]
    public void ResolveLargeImageKey_DoesNotUseErrorAssetForStalledActivity()
    {
        var options = new DiscordOptions
        {
            ActivityImageKeys = new(StringComparer.OrdinalIgnoreCase)
            {
                [nameof(CodexActivityKind.Stalled)] = "rpc_error",
                [nameof(CodexActivityKind.Ready)] = "rpc_sleeping"
            }
        };

        var key = DiscordAssetKeyResolver.ResolveLargeImageKey(
            options,
            CreatePresence(CodexActivityKind.Stalled));

        Assert.Equal("rpc_sleeping", key);
    }

    [Fact]
    public void ResolveLargeImageKey_UsesWaitingAssetWhenThinkingEvidenceIsMissing()
    {
        var key = DiscordAssetKeyResolver.ResolveLargeImageKey(
            new DiscordOptions(),
            CreatePresence(CodexActivityKind.AnalyzingProject) with { IsThinking = false });

        Assert.Equal("rpc_sleeping", key);
    }

    [Fact]
    public void ResolveLargeImageKey_UsesSleepingAssetAfterCompletedImageHold()
    {
        var key = DiscordAssetKeyResolver.ResolveLargeImageKey(
            new DiscordOptions(),
            CreatePresence(CodexActivityKind.Ready) with
            {
                IsSuccessfulCompletion = true,
                WaitingStartedAt = DateTime.UtcNow.AddSeconds(-60)
            });

        Assert.Equal("rpc_sleeping", key);
    }

    [Fact]
    public void ResolveLargeImageKey_UsesConfiguredCompletedImageHoldDuration()
    {
        var key = DiscordAssetKeyResolver.ResolveLargeImageKey(
            new DiscordOptions { CompletedImageHoldSeconds = 10 },
            CreatePresence(CodexActivityKind.Ready) with
            {
                IsSuccessfulCompletion = true,
                WaitingStartedAt = DateTime.UtcNow.AddSeconds(-11)
            });

        Assert.Equal("rpc_sleeping", key);
    }

    [Fact]
    public void ResolveLargeImageReference_UsesExternalSuccessAssetForSuccessfulCompletion()
    {
        var options = new DiscordOptions
        {
            ExternalImageUrls = new(StringComparer.OrdinalIgnoreCase)
            {
                ["rpc_success"] = "https://raw.githubusercontent.com/example/assets/rpc_success.gif"
            }
        };

        var reference = DiscordAssetKeyResolver.ResolveLargeImageReference(
            options,
            CreatePresence(CodexActivityKind.Ready) with
            {
                IsSuccessfulCompletion = true,
                WaitingStartedAt = DateTime.UtcNow.AddSeconds(-30)
            });

        Assert.Equal("https://raw.githubusercontent.com/example/assets/rpc_success.gif", reference);
    }

    [Fact]
    public void ResolveLargeImageReference_UsesDebuggingAssetForTestCommand()
    {
        var options = new DiscordOptions
        {
            ExternalImageUrls = new(StringComparer.OrdinalIgnoreCase)
            {
                ["rpc_debugging"] = "https://raw.githubusercontent.com/example/assets/rpc_debugging.gif"
            }
        };

        var reference = DiscordAssetKeyResolver.ResolveLargeImageReference(
            options,
            CreatePresence(CodexActivityKind.RunningCommand, RunningCommandKind.Test));

        Assert.Equal("https://raw.githubusercontent.com/example/assets/rpc_debugging.gif", reference);
    }

    [Fact]
    public void ResolveLargeImageKey_FallsBackToLargeImageKeyWhenMappingIsMissing()
    {
        var options = new DiscordOptions
        {
            LargeImageKey = "fallback",
            ActivityImageKeys = new(StringComparer.OrdinalIgnoreCase),
            RunningCommandImageKeys = new(StringComparer.OrdinalIgnoreCase)
        };

        var key = DiscordAssetKeyResolver.ResolveLargeImageKey(
            options,
            CreatePresence(CodexActivityKind.AnalyzingProject));

        Assert.Equal("fallback", key);
    }

    [Fact]
    public void ResolveLargeImageReference_UsesExternalUrlForResolvedAsset()
    {
        var options = new DiscordOptions
        {
            ExternalImageUrls = new(StringComparer.OrdinalIgnoreCase)
            {
                ["rpc_thinking"] = "https://raw.githubusercontent.com/example/assets/rpc_thinking.gif"
            }
        };

        var reference = DiscordAssetKeyResolver.ResolveLargeImageReference(
            options,
            CreatePresence(CodexActivityKind.AnalyzingProject));

        Assert.Equal("https://raw.githubusercontent.com/example/assets/rpc_thinking.gif", reference);
    }

    [Fact]
    public void ResolveImageReference_FallsBackToInternalKeyWhenUrlIsInvalid()
    {
        var options = new DiscordOptions
        {
            ExternalImageUrls = new(StringComparer.OrdinalIgnoreCase)
            {
                ["rpc_thinking"] = "not-a-url"
            }
        };

        var reference = DiscordAssetKeyResolver.ResolveImageReference(options, "rpc_thinking");

        Assert.Equal("rpc_thinking", reference);
    }

    private static RenderedPresence CreatePresence(
        CodexActivityKind activityKind,
        RunningCommandKind commandKind = RunningCommandKind.Unknown)
    {
        return new RenderedPresence(
            "details",
            "state",
            null,
            "small",
            [],
            null,
            activityKind,
            commandKind,
            "") with
        {
            IsThinking = activityKind.IsThinking()
        };
    }
}
