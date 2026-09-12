namespace CodexDiscordPresence;

internal sealed class AntigravityActivityEnricher
{
    private static readonly TimeSpan MaxTranscriptOperationAge = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan TimestampSkewTolerance = TimeSpan.FromSeconds(10);
    private readonly string _userProfilePath;
    private readonly AntigravityTranscriptActivityReader _transcriptReader;
    private readonly AntigravityCliConfirmationReader _confirmationReader;

    internal AntigravityActivityEnricher()
        : this(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            new AntigravityTranscriptActivityReader(),
            new AntigravityCliConfirmationReader())
    {
    }

    internal AntigravityActivityEnricher(
        string userProfilePath,
        AntigravityTranscriptActivityReader transcriptReader,
        AntigravityCliConfirmationReader confirmationReader)
    {
        _userProfilePath = Path.GetFullPath(userProfilePath);
        _transcriptReader = transcriptReader;
        _confirmationReader = confirmationReader;
    }

    internal ProviderObservation Enrich(
        ProviderObservation observation,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(observation);

        var transcriptOperation = _transcriptReader.ReadLatest(
            ResolveTranscriptPath(observation));
        var operation = SelectCurrentOperation(observation, transcriptOperation);
        var pendingInput = _confirmationReader.ReadPending(
            observation.ConversationId,
            observation.ObservedAtUtc,
            nowUtc);

        return observation with
        {
            Operation = operation,
            IsWaitingForInput = observation.IsWaitingForInput || pendingInput.IsPending
        };
    }

    private ProviderOperationObservation? SelectCurrentOperation(
        ProviderObservation observation,
        ProviderOperationObservation? transcriptOperation)
    {
        var directOperation = observation.Operation is null
            ? null
            : AntigravityOperationClassifier.Classify(observation.Operation);
        if (directOperation is not null &&
            IsOperationState(observation.AgentState) &&
            IsCurrentOperation(directOperation, observation.ObservedAtUtc) &&
            (!directOperation.IsCompleted || observation.AgentState == ProviderAgentState.ToolUse))
        {
            return directOperation;
        }

        if (transcriptOperation is not null &&
            IsCurrentOperation(transcriptOperation, observation.ObservedAtUtc) &&
            (observation.AgentState == ProviderAgentState.ToolUse ||
             observation.AgentState == ProviderAgentState.Working && !transcriptOperation.IsCompleted))
        {
            return transcriptOperation;
        }

        return null;
    }

    private static bool IsOperationState(ProviderAgentState agentState) =>
        agentState is ProviderAgentState.Thinking or
            ProviderAgentState.Working or
            ProviderAgentState.ToolUse;

    private static bool IsCurrentOperation(
        ProviderOperationObservation operation,
        DateTimeOffset observationAtUtc)
    {
        if (!operation.ObservedAtUtc.HasValue)
        {
            return true;
        }

        var age = observationAtUtc - operation.ObservedAtUtc.Value;
        return age >= -TimestampSkewTolerance && age <= MaxTranscriptOperationAge;
    }

    private string? ResolveTranscriptPath(ProviderObservation observation)
    {
        var candidates = new List<string>();
        AddExistingPath(candidates, observation.TranscriptPath);

        if (!string.IsNullOrWhiteSpace(observation.ArtifactDirectoryPath) &&
            !string.IsNullOrWhiteSpace(observation.ConversationId))
        {
            AddTranscriptCandidates(
                candidates,
                observation.ArtifactDirectoryPath,
                observation.ConversationId);
        }

        if (!string.IsNullOrWhiteSpace(observation.ConversationId) &&
            IsSafeConversationId(observation.ConversationId))
        {
            var conversationDirectory = Path.Combine(
                _userProfilePath,
                ".gemini",
                "antigravity-cli",
                "brain",
                observation.ConversationId);
            AddTranscriptCandidates(candidates, conversationDirectory, null);
        }

        return candidates.FirstOrDefault(File.Exists);
    }

    private static void AddTranscriptCandidates(
        ICollection<string> candidates,
        string rootPath,
        string? conversationId)
    {
        var roots = new[] { rootPath };
        if (!string.IsNullOrWhiteSpace(conversationId) && IsSafeConversationId(conversationId))
        {
            roots = roots
                .Append(Path.Combine(rootPath, conversationId))
                .ToArray();
        }

        foreach (var root in roots)
        {
            AddExistingPath(candidates, root);
            AddExistingPath(candidates, Path.Combine(root, "transcript_full.jsonl"));
            AddExistingPath(candidates, Path.Combine(root, "transcript.jsonl"));
            AddExistingPath(candidates, Path.Combine(root, ".system_generated", "logs", "transcript_full.jsonl"));
            AddExistingPath(candidates, Path.Combine(root, "logs", "transcript_full.jsonl"));
        }
    }

    private static void AddExistingPath(ICollection<string> candidates, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            candidates.Add(Path.GetFullPath(path));
        }
        catch (ArgumentException)
        {
        }
        catch (NotSupportedException)
        {
        }
    }

    private static bool IsSafeConversationId(string conversationId)
    {
        return !conversationId.Contains('/', StringComparison.Ordinal) &&
            !conversationId.Contains('\\', StringComparison.Ordinal) &&
            !conversationId.Contains("..", StringComparison.Ordinal);
    }
}
