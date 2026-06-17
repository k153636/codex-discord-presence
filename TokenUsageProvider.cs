namespace CodexDiscordPresence;

public sealed class TokenUsageProvider
{
    private readonly TokenUsageOptions _options;

    public TokenUsageProvider(TokenUsageOptions options)
    {
        _options = options;
    }

    public TokenUsageSnapshot GetSnapshot()
    {
        if (!_options.Enabled)
        {
            return new TokenUsageSnapshot(null, null);
        }

        return new TokenUsageSnapshot(_options.TotalTokens, _options.EstimatedCostUsd);
    }
}
