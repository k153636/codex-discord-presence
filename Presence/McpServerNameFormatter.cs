namespace CodexDiscordPresence;

internal static class McpServerNameFormatter
{
    private const int MaxDisplayLength = 16;

    public static string? ExtractServerName(string? rawToolName)
    {
        if (string.IsNullOrWhiteSpace(rawToolName))
        {
            return null;
        }

        var normalized = rawToolName.Trim();
        if (normalized.StartsWith("mcp__", StringComparison.OrdinalIgnoreCase))
        {
            var remainder = normalized["mcp__".Length..];
            var separatorIndex = remainder.IndexOf("__", StringComparison.Ordinal);
            return separatorIndex > 0 ? remainder[..separatorIndex] : null;
        }

        var slashIndex = normalized.IndexOf('/');
        if (slashIndex > 0)
        {
            return normalized[..slashIndex];
        }

        var scopeIndex = normalized.IndexOf("::", StringComparison.Ordinal);
        return scopeIndex > 0 ? normalized[..scopeIndex] : null;
    }

    public static string? ExtractToolName(string? rawToolName)
    {
        if (string.IsNullOrWhiteSpace(rawToolName))
        {
            return null;
        }

        var normalized = rawToolName.Trim();
        if (normalized.StartsWith("mcp__", StringComparison.OrdinalIgnoreCase))
        {
            var remainder = normalized["mcp__".Length..];
            var separatorIndex = remainder.LastIndexOf("__", StringComparison.Ordinal);
            return separatorIndex >= 0 && separatorIndex + 2 < remainder.Length
                ? remainder[(separatorIndex + 2)..]
                : remainder;
        }

        var slashIndex = normalized.LastIndexOf('/');
        if (slashIndex >= 0 && slashIndex + 1 < normalized.Length)
        {
            return normalized[(slashIndex + 1)..];
        }

        var scopeIndex = normalized.LastIndexOf("::", StringComparison.Ordinal);
        return scopeIndex >= 0 && scopeIndex + 2 < normalized.Length
            ? normalized[(scopeIndex + 2)..]
            : normalized;
    }

    public static string Format(string? serverName)
    {
        if (string.IsNullOrWhiteSpace(serverName))
        {
            return "";
        }

        var safeName = new string(
            serverName
                .Trim()
                .Replace('_', '-')
                .Where(character => char.IsLetterOrDigit(character) || character is '-' or '.')
                .ToArray());
        if (safeName.Length == 0)
        {
            return "";
        }

        return safeName.Length <= MaxDisplayLength
            ? safeName
            : safeName[..(MaxDisplayLength - 1)] + "…";
    }
}
