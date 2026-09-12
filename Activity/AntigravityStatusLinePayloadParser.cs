using System.Globalization;
using System.Text.Json;

namespace CodexDiscordPresence;

internal sealed class AntigravityStatusLinePayloadParser : IProviderObservationParser
{
    internal const int MaxPayloadBytes = 256 * 1024;
    private const int MaxValueLength = 128;
    private const int MaxJsonDepth = 16;
    private const int MaxQuotaCount = 32;

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
        var quotas = ReadQuotas(root, observedAtUtc);
        var planTier = ReadSafeText(root, "plan_tier") ??
            ReadSafeText(root, "planTier") ??
            ReadSafeText(root, "plan_name") ??
            ReadSafeText(root, "planName");
        var operation = ReadOperation(root);
        if (operation is not null)
        {
            operation = operation with { ObservedAtUtc = observedAtUtc.ToUniversalTime() };
        }
        var observation = new ProviderObservation(
            ProviderObservationSource.AntigravityCli,
            observedAtUtc.ToUniversalTime(),
            agentState,
            model,
            workspace,
            conversationId,
            executionMode,
            contextWindow,
            activeSubagentCount,
            quotas,
            planTier)
        {
            TranscriptPath = ReadPathValueAny(root, "transcript_path", "transcriptPath"),
            ArtifactDirectoryPath = ReadPathValueAny(root, "artifact_directory_path", "artifactDirectoryPath"),
            Operation = operation,
            IsWaitingForInput = ReadBooleanAny(
                root,
                "waiting_for_input",
                "waitingForInput",
                "awaiting_confirmation",
                "awaitingConfirmation",
                "confirmation_pending",
                "confirmationPending")
        };

        return observation;
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

    private static IReadOnlyList<ProviderQuotaObservation>? ReadQuotas(
        JsonElement root,
        DateTimeOffset observedAtUtc)
    {
        if (!TryGetObject(root, "quota", out var quotaObject))
        {
            return null;
        }

        var quotas = new List<ProviderQuotaObservation>();
        foreach (var property in quotaObject.EnumerateObject())
        {
            if (quotas.Count >= MaxQuotaCount)
            {
                break;
            }

            var id = NormalizeQuotaId(property.Name);
            if (id is null || property.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var remainingFraction = ReadFraction(property.Value, "remaining_fraction", "remainingFraction");
            if (!remainingFraction.HasValue)
            {
                continue;
            }

            var resetAtUtc = ReadResetAtUtc(property.Value, observedAtUtc);
            var window = ReadSafeText(property.Value, "window");
            quotas.Add(new ProviderQuotaObservation(id, remainingFraction.Value, resetAtUtc, window));
        }

        return quotas.Count == 0 ? null : quotas;
    }

    private static decimal? ReadFraction(JsonElement root, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!root.TryGetProperty(propertyName, out var value))
            {
                continue;
            }

            decimal parsed;
            if ((value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out parsed)) ||
                (value.ValueKind == JsonValueKind.String &&
                 decimal.TryParse(
                     value.GetString(),
                     NumberStyles.Float,
                     CultureInfo.InvariantCulture,
                     out parsed)))
            {
                return parsed is >= 0m and <= 1m ? parsed : null;
            }
        }

        return null;
    }

    private static DateTimeOffset? ReadResetAtUtc(
        JsonElement root,
        DateTimeOffset observedAtUtc)
    {
        foreach (var propertyName in new[] { "reset_time", "resetTime" })
        {
            var value = ReadSafeText(root, propertyName);
            if (value is not null &&
                DateTimeOffset.TryParse(
                    value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var parsed))
            {
                return parsed.ToUniversalTime();
            }
        }

        foreach (var propertyName in new[] { "reset_in_seconds", "resetInSeconds" })
        {
            var seconds = ReadNonNegativeInt64Value(root, propertyName);
            if (!seconds.HasValue)
            {
                continue;
            }

            try
            {
                return observedAtUtc.ToUniversalTime().AddSeconds(seconds.Value);
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        return null;
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

    private static ProviderOperationObservation? ReadOperation(JsonElement root)
    {
        var containers = new List<JsonElement> { root };
        if (TryGetObject(root, "activity", out var activity))
        {
            containers.Add(activity);
        }

        if (TryGetObject(root, "operation", out var operation))
        {
            containers.Add(operation);
        }

        if (TryGetObject(root, "tool", out var tool))
        {
            containers.Add(tool);
        }

        var toolName = ReadSafeTextFromContainers(
            containers,
            "tool_name",
            "toolName",
            "name",
            "operation_name",
            "operationName");
        var action = ReadSafeTextFromContainers(containers, "tool_action", "toolAction", "action");
        var summary = ReadSafeTextFromContainers(
            containers,
            "tool_summary",
            "toolSummary",
            "summary",
            "command",
            "cmd");
        var targetPath = ReadSafeTextFromContainers(
            containers,
            "target_path",
            "targetPath",
            "file_path",
            "filePath",
            "absolute_path",
            "AbsolutePath",
            "path",
            "url",
            "Url");
        var isCompleted = ReadBooleanFromContainers(containers, "is_completed", "isCompleted", "completed");

        if (toolName is null &&
            TryGetString(root, "tool", out var directToolName))
        {
            toolName = NormalizeText(directToolName);
        }

        if (toolName is null && action is null && summary is null && targetPath is null)
        {
            return null;
        }

        return new ProviderOperationObservation(
            CodexOperationKind.Unknown,
            toolName,
            action,
            summary,
            targetPath,
            isCompleted);
    }

    private static string? ReadPathValueAny(JsonElement root, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (TryGetString(root, propertyName, out var value))
            {
                var trimmed = value.Trim();
                if (trimmed.Length > 0)
                {
                    return trimmed;
                }
            }
        }

        return null;
    }

    private static bool ReadBooleanAny(JsonElement root, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (root.TryGetProperty(propertyName, out var value) &&
                value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                return value.GetBoolean();
            }
        }

        return false;
    }

    private static string? ReadSafeTextFromContainers(
        IReadOnlyList<JsonElement> containers,
        params string[] propertyNames)
    {
        foreach (var container in containers)
        {
            foreach (var propertyName in propertyNames)
            {
                var value = ReadSafeText(container, propertyName);
                if (value is not null)
                {
                    return value;
                }
            }
        }

        return null;
    }

    private static bool ReadBooleanFromContainers(
        IReadOnlyList<JsonElement> containers,
        params string[] propertyNames)
    {
        foreach (var container in containers)
        {
            if (ReadBooleanAny(container, propertyNames))
            {
                return true;
            }
        }

        return false;
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

    private static long? ReadNonNegativeInt64Value(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
        {
            return number >= 0 ? number : null;
        }

        if (value.ValueKind == JsonValueKind.String &&
            long.TryParse(
                value.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out number))
        {
            return number >= 0 ? number : null;
        }

        return null;
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

    private static string? NormalizeQuotaId(string value)
    {
        if (value.Contains('/', StringComparison.Ordinal) ||
            value.Contains('\\', StringComparison.Ordinal))
        {
            return null;
        }

        return NormalizeText(value);
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
