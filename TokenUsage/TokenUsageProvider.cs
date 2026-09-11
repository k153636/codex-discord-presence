namespace CodexDiscordPresence;

public sealed class TokenUsageProvider
{
    private readonly TokenUsageOptions _options;
    private readonly CodexSessionLogParser _sessionLogParser;
    private readonly IBillingTypeProvider? _billingTypeProvider;
    private readonly IRateLimitProvider? _rateLimitProvider;
    private readonly object _billingTypeLock = new();
    private bool _billingTypeResolved;
    private string? _detectedBillingType;

    private static readonly IReadOnlyDictionary<string, ModelPricing> PricingByModel = new Dictionary<string, ModelPricing>(StringComparer.OrdinalIgnoreCase)
    {
        ["gpt-5.5"] = new(5.00m, 0.50m, 30.00m),
        ["gpt-5.4"] = new(2.50m, 0.25m, 15.00m),
        ["gpt-5.4-mini"] = new(0.75m, 0.075m, 4.50m),
        ["gpt-5.4-nano"] = new(0.20m, 0.02m, 1.25m),
        ["gpt-5.5-pro"] = new(30.00m, null, 180.00m),
        ["gpt-5.4-pro"] = new(30.00m, null, 180.00m),
        ["gpt-5.3-codex"] = new(1.75m, 0.175m, 14.00m)
    };

    public TokenUsageProvider(CodexDetectionOptions codexOptions, TokenUsageOptions options)
        : this(codexOptions, options, null, null, null)
    {
    }

    internal TokenUsageProvider(
        CodexDetectionOptions codexOptions,
        TokenUsageOptions options,
        IBillingTypeProvider? billingTypeProvider,
        IRateLimitProvider? rateLimitProvider = null,
        CodexSessionLogParser? sessionLogParser = null)
    {
        _options = options;
        _sessionLogParser = sessionLogParser ?? new CodexSessionLogParser(codexOptions, new PresenceTemplateOptions());
        _billingTypeProvider = billingTypeProvider;
        _rateLimitProvider = rateLimitProvider;
    }

    public TokenUsageSnapshot GetSnapshot(
        string? projectPath = null,
        string? fallbackModelName = null,
        bool includeSessionScan = true,
        CancellationToken cancellationToken = default)
    {
        var billingType = ResolveBillingType(cancellationToken);
        var rateLimit = ResolveRateLimit(billingType, cancellationToken);
        if (!_options.Enabled)
        {
            return new TokenUsageSnapshot(null, null, billingType, rateLimit);
        }

        if (!includeSessionScan)
        {
            return new TokenUsageSnapshot(null, null, billingType, rateLimit);
        }

        var sessionInspection = _sessionLogParser.InspectRecentSessions(projectPath, cancellationToken);
        return BuildSnapshot(sessionInspection, projectPath, fallbackModelName, billingType, rateLimit);
    }

    internal TokenUsageSnapshot GetSnapshotForSession(
        string? projectPath,
        string? fallbackModelName,
        SessionInspection? sessionInspection,
        CancellationToken cancellationToken = default)
    {
        var billingType = ResolveBillingType(cancellationToken);
        var rateLimit = ResolveRateLimit(billingType, cancellationToken);
        return BuildSnapshot(sessionInspection, projectPath, fallbackModelName, billingType, rateLimit);
    }

    private TokenUsageSnapshot BuildSnapshot(
        SessionInspection? sessionInspection,
        string? projectPath,
        string? fallbackModelName,
        string? billingType,
        RateLimitSnapshot? rateLimit)
    {
        if (!_options.Enabled ||
            sessionInspection is null ||
            !sessionInspection.IsPrimaryThread ||
            !MatchesRequestedProject(sessionInspection, projectPath) ||
            sessionInspection.LatestTokenUsage is not { } totals)
        {
            return new TokenUsageSnapshot(null, null, billingType, rateLimit);
        }

        return new TokenUsageSnapshot(
            totals.TotalTokens,
            EstimateLatestCost(
                totals,
                sessionInspection.InitialModelName ?? sessionInspection.ModelName ?? fallbackModelName),
            billingType,
            rateLimit);
    }

    private static bool MatchesRequestedProject(SessionInspection sessionInspection, string? projectPath)
    {
        return string.IsNullOrWhiteSpace(projectPath) || sessionInspection.MatchesProject;
    }

    private RateLimitSnapshot? ResolveRateLimit(
        string? billingType,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(billingType, "subsc", StringComparison.OrdinalIgnoreCase) ||
            _rateLimitProvider is null)
        {
            return null;
        }

        try
        {
            return _rateLimitProvider.GetRateLimit(cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    private string? ResolveBillingType(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_options.BillingType))
        {
            return _options.BillingType;
        }

        if (_billingTypeProvider is null)
        {
            return null;
        }

        lock (_billingTypeLock)
        {
            if (_billingTypeResolved)
            {
                return _detectedBillingType;
            }

            try
            {
                _detectedBillingType = _billingTypeProvider.GetBillingType(cancellationToken);
            }
            catch
            {
                _detectedBillingType = null;
            }

            _billingTypeResolved = true;
            return _detectedBillingType;
        }
    }

    private static bool IsUsableModelName(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
            !value.Contains('{', StringComparison.Ordinal) &&
            !value.Contains('}', StringComparison.Ordinal);
    }

    private static decimal? EstimateLatestCost(CodexTokenUsageTotals totals, string? modelName)
    {
        if (!TryGetPricing(modelName, out var pricing) &&
            !TryGetPricing("gpt-5.4-mini", out pricing))
        {
            return null;
        }

        return pricing?.CalculateCost(totals);
    }

    private static bool TryGetPricing(string? modelName, out ModelPricing? pricing)
    {
        if (modelName is null)
        {
            pricing = null;
            return false;
        }

        var trimmed = modelName.Trim();
        if (!IsUsableModelName(trimmed) ||
            !PricingByModel.TryGetValue(trimmed, out pricing))
        {
            pricing = null;
            return false;
        }

        return true;
    }

    private sealed record ModelPricing(decimal InputPerMillion, decimal? CachedInputPerMillion, decimal OutputPerMillion)
    {
        public decimal? CalculateCost(CodexTokenUsageTotals totals)
        {
            if (CachedInputPerMillion is null && totals.CachedInputTokens > 0)
            {
                return null;
            }

            var outputTokens = totals.OutputTokens + totals.ReasoningOutputTokens;

            return
                (totals.InputTokens * InputPerMillion / 1_000_000m) +
                (totals.CachedInputTokens * (CachedInputPerMillion ?? 0m) / 1_000_000m) +
                (outputTokens * OutputPerMillion / 1_000_000m);
        }
    }
}
