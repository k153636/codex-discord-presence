namespace CodexDiscordPresence;

// Owned by the presence runtime thread; lifecycle observations remain provider-owned.
internal sealed class ClaudeCodeArtworkState(Func<DateTimeOffset>? utcNow = null)
{
    private readonly Func<DateTimeOffset> _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    private string? _sessionId;
    private DateTimeOffset? _lastPromptAtUtc;
    private DateTimeOffset? _requestedAtUtc;
    private DateTimeOffset? _acknowledgedAtUtc;
    private RenderedPresence? _activityPresence;

    internal void Cancel()
    {
        _requestedAtUtc = null;
        _acknowledgedAtUtc = null;
        _activityPresence = null;
    }

    internal RenderedPresence Apply(ClaudeCodeSessionObservation observation, RenderedPresence presence,
        DiscordOptions options, DiscordPresenceSnapshot? publishedPresence)
    {
        var nowUtc = _utcNow();
        ObservePrompt(observation, nowUtc);
        var activityPresence = presence with { LargeImageKeyOverride = null };
        _activityPresence = activityPresence;
        if (presence.ProviderId != ProviderIds.ClaudeCode || !presence.ActivityKind.IsActive() ||
            presence.IsError || observation.Ended)
        {
            Cancel();
        }
        if (_requestedAtUtc is null)
        {
            return activityPresence;
        }
        if (RestoreIfDue(options, publishedPresence) is { } restored)
        {
            return restored;
        }
        return presence with { LargeImageKeyOverride = ClaudeCodeAssetPolicy.NotificationImageKey };
    }

    internal RenderedPresence? RestoreIfDue(DiscordOptions options, DiscordPresenceSnapshot? publishedPresence)
    {
        if (_requestedAtUtc is not { } requestedAtUtc) return null;
        var nowUtc = _utcNow();
        RecordAcknowledgment(publishedPresence, options, nowUtc);
        var expired = _acknowledgedAtUtc is { } acceptedAtUtc
            ? nowUtc - acceptedAtUtc >= ClaudeCodeAssetPolicy.NotificationLoopDuration
            : nowUtc - requestedAtUtc >= DiscordPresenceClient.ResponseTimeout;
        if (!expired) return null;
        var restored = _activityPresence;
        Cancel();
        return restored;
    }

    private void ObservePrompt(ClaudeCodeSessionObservation observation, DateTimeOffset nowUtc)
    {
        if (_sessionId != observation.SessionId)
        {
            Cancel();
            _sessionId = observation.SessionId;
            _lastPromptAtUtc = null;
        }
        if (observation.LastUserPromptAtUtc is { } promptAtUtc && promptAtUtc != _lastPromptAtUtc)
        {
            Cancel();
            _lastPromptAtUtc = promptAtUtc;
            var age = nowUtc - promptAtUtc;
            if (age >= TimeSpan.Zero && age < DiscordPresenceClient.ResponseTimeout)
            {
                _requestedAtUtc = nowUtc;
            }
        }
    }

    private void RecordAcknowledgment(DiscordPresenceSnapshot? publishedPresence, DiscordOptions options, DateTimeOffset nowUtc)
    {
        if (_requestedAtUtc is { } requestedAtUtc && _acknowledgedAtUtc is null && publishedPresence is
            { ProviderId: ProviderIds.ClaudeCode, AcknowledgedAtUtc: { } acknowledgedAtUtc } &&
            acknowledgedAtUtc >= requestedAtUtc.UtcDateTime && acknowledgedAtUtc <= nowUtc.UtcDateTime &&
            string.Equals(publishedPresence.LargeImageKey,
                DiscordAssetKeyResolver.ResolveImageReference(options, ClaudeCodeAssetPolicy.NotificationImageKey),
                StringComparison.Ordinal))
        {
            _acknowledgedAtUtc = new DateTimeOffset(acknowledgedAtUtc);
        }
    }

    internal TimeSpan GetNextDelay(TimeSpan defaultDelay)
    {
        if (_requestedAtUtc is null) return defaultDelay;
        var untilUtc = _acknowledgedAtUtc is { } acceptedAtUtc
            ? acceptedAtUtc + ClaudeCodeAssetPolicy.NotificationLoopDuration
            : _requestedAtUtc.Value + DiscordPresenceClient.ResponseTimeout;
        var remaining = untilUtc - _utcNow();
        var delay = remaining > TimeSpan.Zero ? remaining : TimeSpan.FromMilliseconds(1);
        return TimeSpan.FromTicks(Math.Min(defaultDelay.Ticks, Math.Min(delay.Ticks, TimeSpan.TicksPerSecond)));
    }
}
