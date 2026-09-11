namespace CodexDiscordPresence;

internal sealed class AntigravityRuntimeState
{
    private const int MaxConversationStates = 64;
    private const string UnknownConversationKey = "<unknown>";
    private readonly Dictionary<string, ConversationPresenceState> _conversationStates =
        new(StringComparer.Ordinal);

    internal string? CurrentConversationId { get; private set; }

    internal AntigravityConversationRuntimeState GetPresenceCache(string? conversationId)
    {
        var key = NormalizeConversationKey(conversationId);
        if (!_conversationStates.TryGetValue(key, out var state))
        {
            state = new ConversationPresenceState(new AntigravityConversationRuntimeState());
            _conversationStates[key] = state;
        }

        state.LastUsedUtc = DateTime.UtcNow;
        CurrentConversationId = key == UnknownConversationKey ? null : key;
        TrimConversationStates();
        return state.Cache;
    }

    internal void ResetPresenceCaches()
    {
        foreach (var state in _conversationStates.Values)
        {
            state.Cache.ResetPresenceCache();
        }

        _conversationStates.Clear();
        CurrentConversationId = null;
    }

    private void TrimConversationStates()
    {
        while (_conversationStates.Count > MaxConversationStates)
        {
            var leastRecentlyUsed = _conversationStates
                .OrderBy(pair => pair.Value.LastUsedUtc)
                .First();
            _conversationStates.Remove(leastRecentlyUsed.Key);
        }
    }

    private static string NormalizeConversationKey(string? conversationId)
    {
        return string.IsNullOrWhiteSpace(conversationId)
            ? UnknownConversationKey
            : conversationId.Trim();
    }

    private sealed class ConversationPresenceState
    {
        internal ConversationPresenceState(AntigravityConversationRuntimeState cache)
        {
            Cache = cache;
        }

        internal AntigravityConversationRuntimeState Cache { get; }
        internal DateTime LastUsedUtc { get; set; } = DateTime.UtcNow;
    }
}

internal sealed class AntigravityConversationRuntimeState : PresenceDispatchCache
{
    internal AntigravityActivitySnapshot? LastActivity { get; set; }
}
