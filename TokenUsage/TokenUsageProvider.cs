using System.Globalization;
using System.Text.Json;

namespace CodexDiscordPresence;

public sealed class TokenUsageProvider
{
    private readonly CodexDetectionOptions _codexOptions;
    private readonly TokenUsageOptions _options;
    private readonly string _codexHomePath;

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
    {
        _codexOptions = codexOptions;
        _options = options;
        _codexHomePath = codexOptions.GetResolvedHomePath();
    }

    public TokenUsageSnapshot GetSnapshot(string? projectPath = null, string? fallbackModelName = null)
    {
        if (!_options.Enabled)
        {
            return new TokenUsageSnapshot(null, null);
        }

        var inspection = InspectRecentSessions(projectPath, fallbackModelName);
        if (inspection is null)
        {
            return new TokenUsageSnapshot(null, null);
        }

        return new TokenUsageSnapshot(inspection.TotalTokens, inspection.EstimatedCostUsd);
    }

    private SessionUsageInspection? InspectRecentSessions(string? projectPath, string? fallbackModelName)
    {
        var sessionsPath = Path.Combine(_codexHomePath, "sessions");
        if (!Directory.Exists(sessionsPath))
        {
            return null;
        }

        var normalizedProjectPath = string.IsNullOrWhiteSpace(projectPath)
            ? null
            : NormalizePath(projectPath);

        var recentFiles = Directory
            .EnumerateFiles(sessionsPath, "*.jsonl", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Take(Math.Max(1, _codexOptions.RecentSessionFilesToScan));

        foreach (var file in recentFiles)
        {
            var inspection = AnalyzeSessionFile(file.FullName, normalizedProjectPath, fallbackModelName);
            if (inspection.MatchesProject && inspection.HasTokenUsage)
            {
                return inspection;
            }
        }

        return null;
    }

    private SessionUsageInspection AnalyzeSessionFile(string path, string? normalizedProjectPath, string? fallbackModelName)
    {
        var matchesProject = false;
        var hasTokenUsage = false;
        string? stableModel = null;
        TokenUsageTotals? latestTotals = null;
        long? latestTotalTokens = null;

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);

            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                if (!line.Contains("\"payload\"", StringComparison.Ordinal))
                {
                    continue;
                }

                using var document = JsonDocument.Parse(line);
                if (!document.RootElement.TryGetProperty("payload", out var payload))
                {
                    continue;
                }

                var rootType = TryGetString(document.RootElement, "type");
                var payloadType = TryGetString(payload, "type");

                if (rootType is "session_meta" or "turn_context")
                {
                    if (TryGetString(payload, "cwd", out var cwd) &&
                        normalizedProjectPath is not null &&
                        NormalizePath(cwd) == normalizedProjectPath)
                    {
                        matchesProject = true;
                    }

                    stableModel ??= TryResolveModel(payload);
                    continue;
                }

                if (payloadType != "token_count")
                {
                    continue;
                }

                if (!TryReadTokenTotals(payload, out var totals))
                {
                    continue;
                }

                hasTokenUsage = true;
                latestTotalTokens = totals.TotalTokens;
                latestTotals = totals;
            }
        }
        catch
        {
            return new SessionUsageInspection(false, false, null, null);
        }

        decimal? estimatedCost = null;
        if (hasTokenUsage)
        {
            estimatedCost = EstimateLatestCost(latestTotals, stableModel ?? fallbackModelName);
        }

        return new SessionUsageInspection(
            matchesProject,
            hasTokenUsage,
            latestTotalTokens,
            estimatedCost);
    }

    private static bool TryReadTokenTotals(JsonElement payload, out TokenUsageTotals totals)
    {
        totals = default;

        if (!payload.TryGetProperty("info", out var info) ||
            !info.TryGetProperty("total_token_usage", out var tokenUsage))
        {
            return false;
        }

        if (!TryReadLong(tokenUsage, "input_tokens", out var inputTokens) ||
            !TryReadLong(tokenUsage, "cached_input_tokens", out var cachedInputTokens) ||
            !TryReadLong(tokenUsage, "output_tokens", out var outputTokens) ||
            !TryReadLong(tokenUsage, "reasoning_output_tokens", out var reasoningOutputTokens) ||
            !TryReadLong(tokenUsage, "total_tokens", out var totalTokens))
        {
            return false;
        }

        totals = new TokenUsageTotals(inputTokens, cachedInputTokens, outputTokens, reasoningOutputTokens, totalTokens);
        return true;
    }

    private static string? TryResolveModel(JsonElement payload)
    {
        if (TryGetString(payload, "model", out var directModel) && IsUsableModelName(directModel))
        {
            return directModel;
        }

        if (payload.TryGetProperty("collaboration_mode", out var collaborationMode) &&
            collaborationMode.TryGetProperty("settings", out var settings) &&
            TryGetString(settings, "model", out var collaborationModel) &&
            IsUsableModelName(collaborationModel))
        {
            return collaborationModel;
        }

        return null;
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        value = "";

        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? "";
        return true;
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        return TryGetString(element, propertyName, out var value) ? value : null;
    }

    private static bool TryReadLong(JsonElement element, string propertyName, out long value)
    {
        value = 0;

        if (!element.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out value))
        {
            return true;
        }

        if (property.ValueKind == JsonValueKind.String &&
            long.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        return false;
    }

    private static bool IsUsableModelName(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
            !value.Contains('{', StringComparison.Ordinal) &&
            !value.Contains('}', StringComparison.Ordinal);
    }

    private static decimal? EstimateLatestCost(TokenUsageTotals? totals, string? modelName)
    {
        if (totals is null)
        {
            return null;
        }

        var totalsValue = totals.Value;
        if (!TryGetPricing(modelName, out var pricing) &&
            !TryGetPricing("gpt-5.4-mini", out pricing))
        {
            return null;
        }

        return pricing?.CalculateCost(totalsValue);
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

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .ToUpperInvariant();
        }
        catch
        {
            return path.Trim().ToUpperInvariant();
        }
    }

    private sealed record SessionUsageInspection(
        bool MatchesProject,
        bool HasTokenUsage,
        long? TotalTokens,
        decimal? EstimatedCostUsd);

    private readonly record struct TokenUsageTotals(
        long InputTokens,
        long CachedInputTokens,
        long OutputTokens,
        long ReasoningOutputTokens,
        long TotalTokens)
    {
        public bool IsValid =>
            InputTokens >= 0 &&
            CachedInputTokens >= 0 &&
            OutputTokens >= 0 &&
            ReasoningOutputTokens >= 0 &&
            TotalTokens >= 0;

        public static TokenUsageTotals operator -(TokenUsageTotals left, TokenUsageTotals right)
        {
            return new TokenUsageTotals(
                left.InputTokens - right.InputTokens,
                left.CachedInputTokens - right.CachedInputTokens,
                left.OutputTokens - right.OutputTokens,
                left.ReasoningOutputTokens - right.ReasoningOutputTokens,
                left.TotalTokens - right.TotalTokens);
        }
    }

    private sealed record ModelPricing(decimal InputPerMillion, decimal? CachedInputPerMillion, decimal OutputPerMillion)
    {
        public decimal? CalculateCost(TokenUsageTotals totals)
        {
            if (totals.InputTokens < 0 ||
                totals.CachedInputTokens < 0 ||
                totals.OutputTokens < 0 ||
                totals.ReasoningOutputTokens < 0 ||
                totals.TotalTokens < 0)
            {
                return null;
            }

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
