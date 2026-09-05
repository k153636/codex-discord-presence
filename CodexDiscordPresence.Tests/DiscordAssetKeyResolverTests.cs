using CodexDiscordPresence;

namespace CodexDiscordPresence.Tests;

public sealed class DiscordAssetKeyResolverTests
{
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
    public void ResolveLargeImageKey_UsesActivityMapping(CodexActivityKind activityKind, string expectedKey)
    {
        var presence = CreatePresence(activityKind);

        var key = DiscordAssetKeyResolver.ResolveLargeImageKey(new DiscordOptions(), presence);

        Assert.Equal(expectedKey, key);
    }

    [Theory]
    [InlineData(RunningCommandKind.Unknown, "rpc_building")]
    [InlineData(RunningCommandKind.Git, "rpc_reading")]
    [InlineData(RunningCommandKind.Search, "rpc_searching")]
    [InlineData(RunningCommandKind.Build, "rpc_building")]
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
            "");
    }
}
