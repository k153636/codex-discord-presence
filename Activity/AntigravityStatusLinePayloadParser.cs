using System.Text.Json;

namespace CodexDiscordPresence;

internal sealed class AntigravityStatusLinePayloadParser : IProviderObservationParser
{
    internal const int MaxPayloadBytes = 256 * 1024;
    private const int MaxValueLength = 128;
    private const int MaxJsonDepth = 16;

    public bool TryParse(
        ReadOnlySpan<byte> utf8Json,
        DateTimeOffset observedAtUtc,
        out ProviderObservation? observation)
    {
        observation = null;
        if (utf8Json.Length == 0 || utf8Json.Length > MaxPayloadBytes)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(
                utf8Json.ToArray(),
                new JsonDocumentOptions
                {
                    MaxDepth = MaxJsonDepth,
                    CommentHandling = JsonCommentHandling.Disallow
                });

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            observation = CreateObservation(document.RootElement, observedAtUtc);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static ProviderObservation CreateObservation(
        JsonElement root,
        DateTimeOffset observedAtUtc)
    {
        var model = CreateModel(root);
        var workspace = CreateWorkspace(root);
        var conversationId = ReadConversationId(root);
        var agentState = ReadAgentState(root);
        var executionMode = ReadExecutionMode(root);
        var contextWindow = ReadContextWindow(root);
        var activeSubagentCount = ReadActiveSubagentCount(root);

        return new ProviderObservation(
            ProviderObservationSource.AntigravityCli,
            observedAtUtc.ToUniversalTime(),
            agentState,
            model,
            workspace,
            conversationId,
            executionMode,
            contextWindow,
            activeSubagentCount);
    }

    private static ProviderModelObservation? CreateModel(JsonElement root)
    {
        if (!TryGetObject(root, "model", out var modelElement))
        {
            return null;
        }

        var id = ReadSafeText(modelElement, "id");
        var displayName = ReadSafeText(modelElement, "display_name");
        return id is null && displayName is null
            ? null
            : new ProviderModelObservation(id, displayName);
    }

    private static ProviderWorkspaceObservation? CreateWorkspace(JsonElement root)
    {
        var cwd = ReadPathValue(root, "cwd");
        var workspaceCurrentDirectory = ReadPathValue(root, "workspace", "current_dir");
        var projectDirectory = ReadPathValue(root, "workspace", "project_dir");

        return cwd is null && workspaceCurrentDirectory is null && projectDirectory is null
            ? null
            : new ProviderWorkspaceObservation(cwd, workspaceCurrentDirectory, projectDirectory);
    }

    private static string? ReadConversationId(JsonElement root)
    {
        return ReadSafeIdentifier(root, "conversation_id");
    }

    private static ProviderAgentState ReadAgentState(JsonElement root)
    {
        var value = ReadSafeText(root, "agent_state");
        return value?.ToLowerInvariant() switch
        {
            "idle" => ProviderAgentState.Idle,
            "thinking" => ProviderAgentState.Thinking,
            "working" => ProviderAgentState.Working,
            "tool_use" => ProviderAgentState.ToolUse,
            "initializing" => ProviderAgentState.Initializing,
            _ => ProviderAgentState.Unknown
        };
    }

    private static ProviderExecutionMode ReadExecutionMode(JsonElement root)
    {
        var value = ReadSafeText(root, "execution_mode");
        return value?.ToLowerInvariant() switch
        {
            "planning" => ProviderExecutionMode.Planning,
            "fast" => ProviderExecutionMode.Fast,
            _ => ProviderExecutionMode.Unknown
        };
    }

    private static ProviderContextWindowObservation? ReadContextWindow(JsonElement root)
    {
        if (!TryGetObject(root, "context_window", out var contextWindow))
        {
            return null;
        }

        var totalInputTokens = ReadNonNegativeInt64(contextWindow, "total_input_tokens");
        var totalOutputTokens = ReadNonNegativeInt64(contextWindow, "total_output_tokens");
        return totalInputTokens is null && totalOutputTokens is null
            ? null
            : new ProviderContextWindowObservation(totalInputTokens, totalOutputTokens);
    }

    private static int? ReadActiveSubagentCount(JsonElement root)
    {
        if (!root.TryGetProperty("subagents", out var subagents) ||
            subagents.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var count = 0;
        foreach (var subagent in subagents.EnumerateArray())
        {
            if (subagent.ValueKind != JsonValueKind.Object ||
                !IsActiveSubagent(subagent) ||
                !HasSubagentIdentity(subagent))
            {
                continue;
            }

            count++;
            if (count == ProviderObservation.MaxActiveSubagentCount)
            {
                break;
            }
        }

        return count;
    }

    private static bool IsActiveSubagent(JsonElement subagent)
    {
        var status = ReadSafeText(subagent, "status")?.ToLowerInvariant();
        return status is
            "running" or
            "active" or
            "thinking" or
            "working" or
            "tool_use" or
            "initializing";
    }

    private static bool HasSubagentIdentity(JsonElement subagent)
    {
        return ReadSafeText(subagent, "id") is not null ||
            ReadSafeText(subagent, "conversation_id") is not null ||
            ReadSafeText(subagent, "name") is not null ||
            ReadSafeText(subagent, "role") is not null;
    }

    private static long? ReadNonNegativeInt64(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt64(out var number) ||
            number < 0)
        {
            return null;
        }

        return number;
    }

    private static string? ReadPathValue(
        JsonElement root,
        string parentPropertyName,
        string? childPropertyName = null)
    {
        var element = root;
        if (childPropertyName is not null)
        {
            if (!TryGetObject(root, parentPropertyName, out element))
            {
                return null;
            }

            parentPropertyName = childPropertyName;
        }

        if (!TryGetString(element, parentPropertyName, out var value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static string? ReadSafeIdentifier(JsonElement root, string propertyName)
    {
        if (!TryGetString(root, propertyName, out var value) ||
            value.Contains('/', StringComparison.Ordinal) ||
            value.Contains('\\', StringComparison.Ordinal))
        {
            return null;
        }

        return NormalizeText(value);
    }

    private static string? ReadSafeText(JsonElement root, string propertyName)
    {
        return TryGetString(root, propertyName, out var value)
            ? NormalizeText(value)
            : null;
    }

    private static bool TryGetObject(
        JsonElement root,
        string propertyName,
        out JsonElement value)
    {
        if (root.TryGetProperty(propertyName, out value) &&
            value.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        value = default;
        return false;
    }

    private static bool TryGetString(
        JsonElement root,
        string propertyName,
        out string value)
    {
        if (root.TryGetProperty(propertyName, out var element) &&
            element.ValueKind == JsonValueKind.String &&
            element.GetString() is { } stringValue)
        {
            value = stringValue;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static string? NormalizeText(string value)
    {
        var withoutControlCharacters = new string(
            value
                .Trim()
                .Where(character => !char.IsControl(character))
                .ToArray());
        var normalized = string.Join(
            ' ',
            withoutControlCharacters.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        if (normalized.Length == 0)
        {
            return null;
        }

        if (normalized.Length <= MaxValueLength)
        {
            return normalized;
        }

        var truncated = normalized[..MaxValueLength];
        return char.IsHighSurrogate(truncated[^1])
            ? truncated[..^1]
            : truncated;
    }
}
