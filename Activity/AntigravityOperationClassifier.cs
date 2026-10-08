namespace CodexDiscordPresence;

internal static class AntigravityOperationClassifier
{
    public static ProviderOperationObservation Classify(ProviderOperationObservation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (operation.Kind != CodexOperationKind.Unknown)
        {
            return operation;
        }

        var normalizedToolName = Normalize(operation.ToolName);
        var normalizedDescription = Normalize(operation.Action ?? operation.Summary);
        var kind = ClassifyTool(normalizedToolName, normalizedDescription);
        return operation with { Kind = kind };
    }

    private static CodexOperationKind ClassifyTool(
        string toolName,
        string description)
    {
        if (toolName is "searchweb" or "readurlcontent" or "browsersearch" or "websearch" ||
            description.Contains("search", StringComparison.Ordinal) ||
            description.Contains("web", StringComparison.Ordinal) ||
            description.Contains("url", StringComparison.Ordinal))
        {
            return CodexOperationKind.Research;
        }

        if (toolName is "viewfile" or "readfile" or "grepsearch" or "listfiles" or "glob" ||
            description.Contains("read", StringComparison.Ordinal) ||
            description.Contains("inspect", StringComparison.Ordinal) ||
            description.Contains("grep", StringComparison.Ordinal))
        {
            return CodexOperationKind.Read;
        }

        if (toolName is "applypatch" or "editfile" or "writefile" or "replacefile" or "modifyfile")
        {
            return CodexOperationKind.Edit;
        }

        if (toolName is "createfile" or "write_new_file")
        {
            return CodexOperationKind.Create;
        }

        if (toolName is "deletefile" or "removefile")
        {
            return CodexOperationKind.Delete;
        }

        if (toolName is "shellcommand" or "runcommand" or "execcommand" or "terminal")
        {
            return CodexOperationKind.Command;
        }

        return CodexOperationKind.Unknown;
    }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? ""
            : new string(value
                .Trim()
                .Where(character => !char.IsControl(character))
                .ToArray())
                .Replace("_", "", StringComparison.Ordinal)
                .Replace("-", "", StringComparison.Ordinal)
                .Replace(" ", "", StringComparison.Ordinal)
                .ToLowerInvariant();
    }
}
