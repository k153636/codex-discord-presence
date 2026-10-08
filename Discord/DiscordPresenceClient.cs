using DiscordRPC;

namespace CodexDiscordPresence;

public sealed class DiscordPresenceClient : IDisposable
{
    private DiscordOptions _options;
    private readonly DiagnosticLog _log;
    private readonly Func<string, IDiscordPresenceTransport> _transportFactory;
    private readonly Func<DateTime> _utcNow;
    private IDiscordPresenceTransport? _client;
    private bool _isReady;
    private bool _disposed;
    private DateTime? _connectionStartedUtc;
    private PendingPresence? _pendingPresence;
    internal static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(30);
    private sealed record PendingPresence(DiscordPresenceSnapshot? Presence, DateTime SentAtUtc);
    private bool _clearPending;
    private bool _clearSentForCurrentConnection;
    private bool _needsPresenceRefresh = true;
    private readonly DiscordPresenceUpdateThrottle _updateThrottle = new();
    private DateTime? _lastRateLimitLogUtc;
    private DateTime _nextInitializeAttemptUtc = DateTime.MinValue;
    private int _failedInitializeAttempts;
    private readonly string _partyId = $"codex-party-{Guid.NewGuid():N}";

    public DiscordPresenceClient(DiscordOptions options, DiagnosticLog log)
        : this(options, log, clientId => new DiscordPresenceTransport(clientId), () => DateTime.UtcNow)
    {
    }

    internal DiscordPresenceClient(
        DiscordOptions options,
        DiagnosticLog log,
        Func<string, IDiscordPresenceTransport> transportFactory,
        Func<DateTime> utcNow)
    {
        _options = options;
        _log = log;
        _transportFactory = transportFactory ?? throw new ArgumentNullException(nameof(transportFactory));
        _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TryInitialize();
        ProcessPendingNotifications();
        return Task.CompletedTask;
    }

    public bool NeedsPresenceRefresh => _needsPresenceRefresh;

    public bool IsConnected => _isReady;

    public bool IsConnecting => !_disposed && _client is not null && !_isReady;

    public DiscordPresenceSnapshot? LastPublishedPresence { get; private set; }

    internal void RequestPresenceRefresh()
    {
        ProcessPendingNotifications();
        _needsPresenceRefresh = true;
    }

    public void UpdateOptions(DiscordOptions options)
    {
        ProcessPendingNotifications();
        if (string.Equals(_options.ClientId, options.ClientId, StringComparison.Ordinal) &&
            string.Equals(_options.LargeImageKey, options.LargeImageKey, StringComparison.Ordinal) &&
            string.Equals(_options.SmallImageKey, options.SmallImageKey, StringComparison.Ordinal) &&
            string.Equals(_options.CompletedImageKey, options.CompletedImageKey, StringComparison.Ordinal) &&
            _options.CompletedImageHoldSeconds == options.CompletedImageHoldSeconds &&
            string.Equals(_options.ErrorImageKey, options.ErrorImageKey, StringComparison.Ordinal) &&
            AssetMappingsEqual(_options.ActivityImageKeys, options.ActivityImageKeys) &&
            AssetMappingsEqual(_options.RunningCommandImageKeys, options.RunningCommandImageKeys) &&
            AssetMappingsEqual(_options.ExternalImageUrls, options.ExternalImageUrls))
        {
            return;
        }

        var clientIdChanged = !string.Equals(_options.ClientId, options.ClientId, StringComparison.Ordinal);
        _options = options;

        if (clientIdChanged)
        {
            _isReady = false;
            ResetClient();
            LastPublishedPresence = null;
            _clearPending = false;
            _clearSentForCurrentConnection = false;
            _failedInitializeAttempts = 0;
            _nextInitializeAttemptUtc = DateTime.MinValue;
            _updateThrottle.Reset();
            _lastRateLimitLogUtc = null;
        }

        _needsPresenceRefresh = true;
    }

    public bool Update(RenderedPresence presence)
    {
        if (!EnsureReady())
        {
            return false;
        }

        var client = _client;
        if (client is null)
        {
            return false;
        }

        try
        {
            if (_clearPending)
            {
                Clear();
                if (_clearPending)
                {
                    return false;
                }

                if (!_isReady || _client is null)
                {
                    return false;
                }

                client = _client;
            }

            if (_pendingPresence is not null)
            {
                return false;
            }

            var publishedPresence = DiscordRichPresenceBuilder.Create(_options, presence, _partyId);
            var nowUtc = _utcNow();
            if (!_updateThrottle.TryReserve(nowUtc, out var retryAtUtc))
            {
                LogRateLimitDeferral(nowUtc, retryAtUtc);
                return false;
            }

            _pendingPresence = new(DiscordPresenceSnapshot.From(publishedPresence), nowUtc);
            client.SetPresence(publishedPresence);
            _clearSentForCurrentConnection = false;
            _needsPresenceRefresh = false;
            _lastRateLimitLogUtc = null;
            ProcessPendingNotifications();
            return _isReady;
        }
        catch (Exception ex)
        {
            ScheduleReconnect("Discord RPC update failed.", ex);
            return false;
        }
    }

    public void Clear()
    {
        ProcessPendingNotifications();
        if (_disposed)
        {
            return;
        }

        if (_pendingPresence is not null)
        {
            _clearPending = _pendingPresence.Presence is not null;
            _needsPresenceRefresh = true;
            return;
        }

        if (!_isReady || _client is null)
        {
            _clearPending = true;
            _needsPresenceRefresh = true;
            return;
        }

        if (_clearSentForCurrentConnection && LastPublishedPresence is null && !_clearPending)
        {
            _needsPresenceRefresh = true;
            return;
        }

        var nowUtc = _utcNow();
        if (!_updateThrottle.TryReserve(nowUtc, out var retryAtUtc))
        {
            _clearPending = true;
            _needsPresenceRefresh = true;
            LogRateLimitDeferral(nowUtc, retryAtUtc);
            return;
        }

        try
        {
            _pendingPresence = new(null, nowUtc);
            _client.ClearPresence();
            _clearPending = false;
            _clearSentForCurrentConnection = true;
            _needsPresenceRefresh = true;
            _lastRateLimitLogUtc = null;
            ProcessPendingNotifications();
        }
        catch (Exception ex)
        {
            _clearPending = true;
            ScheduleReconnect("Discord RPC clear failed.", ex);
        }
    }

    public void Dispose()
    {
        _disposed = true;
        ResetClient();
        _isReady = false;
        LastPublishedPresence = null;
        _clearPending = false;
        _clearSentForCurrentConnection = false;
        _needsPresenceRefresh = true;
    }

    // Called by the runtime thread even when disabled or no provider can publish.
    internal void MaintainConnection()
    {
        ProcessPendingNotifications();
        if (!_disposed && _client is null && _utcNow() >= _nextInitializeAttemptUtc)
        {
            TryInitialize();
            ProcessPendingNotifications();
        }
    }

    // Transport callbacks only enqueue immutable notifications and never mutate client state.
    internal void ProcessPendingNotifications()
    {
        var client = _client;
        while (client is not null && ReferenceEquals(client, _client) &&
            client.TryDequeueNotification(out var notification))
        {
            if (notification is null)
            {
                continue;
            }

            switch (notification.Kind)
            {
                case DiscordPresenceNotificationKind.Connecting:
                    _isReady = false;
                    _pendingPresence = null;
                    LastPublishedPresence = null;
                    _connectionStartedUtc = _utcNow();
                    _needsPresenceRefresh = true;
                    break;
                case DiscordPresenceNotificationKind.Ready:
                    _isReady = true;
                    _connectionStartedUtc = null;
                    _failedInitializeAttempts = 0;
                    _nextInitializeAttemptUtc = DateTime.MinValue;
                    _pendingPresence = null;
                    LastPublishedPresence = null;
                    _clearSentForCurrentConnection = false;
                    _needsPresenceRefresh = true;
                    _log.Info("Discord RPC initialized. Connection ready.");
                    break;
                case DiscordPresenceNotificationKind.Closed:
                    ScheduleReconnect("Discord RPC connection closed.");
                    break;
                case DiscordPresenceNotificationKind.Error:
                    ScheduleReconnect($"Discord RPC update failed asynchronously (code {notification.ErrorCode}).", error: true);
                    break;
                case DiscordPresenceNotificationKind.PresenceAcknowledged:
                    AcceptAcknowledgment(notification.Presence);
                    break;
            }
        }

        var waitingSinceUtc = _pendingPresence?.SentAtUtc ?? _connectionStartedUtc;
        if (waitingSinceUtc.HasValue && _utcNow() - waitingSinceUtc.Value >= ResponseTimeout)
        {
            ScheduleReconnect("Discord RPC response timed out.");
        }
    }

    private void AcceptAcknowledgment(DiscordPresenceSnapshot? presence)
    {
        if (!_isReady || _pendingPresence is null || !MatchesAcknowledgment(_pendingPresence.Presence, presence))
        {
            return;
        }

        var requested = _pendingPresence.Presence;
        LastPublishedPresence = presence is not null && requested is not null
            ? presence with
            {
                LargeImageKey = ResolveAcknowledgedImageKey(requested.LargeImageKey, presence.LargeImageKey),
                SmallImageKey = ResolveAcknowledgedImageKey(requested.SmallImageKey, presence.SmallImageKey)
            }
            : presence;
        _pendingPresence = null;
        _log.Info(presence is null ? "Discord RPC presence clear acknowledged." : "Discord RPC presence acknowledged.");
    }

    private static string? ResolveAcknowledgedImageKey(string? requested, string? acknowledged)
    {
        // Discord returns uploaded assets as numeric IDs and external URLs as mp: IDs.
        // Retain their request alias only when this response actually contains an asset.
        if (!string.IsNullOrWhiteSpace(requested) && !string.IsNullOrWhiteSpace(acknowledged) &&
            (acknowledged.All(char.IsAsciiDigit) || acknowledged.StartsWith("mp:", StringComparison.Ordinal)))
        {
            return requested;
        }

        return acknowledged;
    }

    private static bool MatchesAcknowledgment(DiscordPresenceSnapshot? expected, DiscordPresenceSnapshot? actual)
    {
        if (expected is null || actual is null)
        {
            return expected is null && actual is null;
        }

        // Discord resolves asset keys to CDN IDs and omits button URLs. Compare the
        // response fields it preserves; serialize requests because the SDK drops nonces.
        return expected.Details == actual.Details && expected.State == actual.State &&
            expected.ActivityType == actual.ActivityType &&
            ToUnixTimeSeconds(expected.StartedAtUtc) == ToUnixTimeSeconds(actual.StartedAtUtc) &&
            expected.PartySize == actual.PartySize && expected.PartyMax == actual.PartyMax;
    }

    private static long? ToUnixTimeSeconds(DateTime? value) =>
        value.HasValue ? new DateTimeOffset(value.Value).ToUnixTimeSeconds() : null;

    private bool EnsureReady()
    {
        MaintainConnection();
        return _isReady;
    }

    private void TryInitialize()
    {
        if (_disposed || _client is not null)
        {
            return;
        }

        if (IsMissingClientId(_options.ClientId))
        {
            ScheduleReconnect("Discord RPC client id is not configured for the current profile.");
            return;
        }

        try
        {
            _client = _transportFactory(_options.ClientId);
            _connectionStartedUtc = _utcNow();
            if (!_client.Initialize())
            {
                ScheduleReconnect("Discord RPC initialization did not start.");
            }
        }
        catch (Exception ex)
        {
            ScheduleReconnect("Discord RPC initialization failed.", ex);
        }
    }

    private void ScheduleReconnect(string message, Exception? exception = null, bool error = false)
    {
        _isReady = false;
        ResetClient();
        LastPublishedPresence = null;
        _clearSentForCurrentConnection = false;
        _needsPresenceRefresh = true;
        _failedInitializeAttempts = Math.Min(_failedInitializeAttempts + 1, int.MaxValue);
        var delay = DiscordReconnectBackoff.GetDelay(_failedInitializeAttempts);
        _nextInitializeAttemptUtc = _utcNow().Add(delay);
        var detail = $"{message} Reconnecting in {delay.TotalSeconds:0}s.";
        if (exception is not null)
        {
            _log.Error(detail, exception);
        }
        else if (error)
        {
            _log.Error(detail);
        }
        else
        {
            _log.Warn(detail);
        }
    }

    private void ResetClient()
    {
        var client = _client;
        _client = null;
        _pendingPresence = null;
        _connectionStartedUtc = null;
        client?.Dispose();
    }

    private void LogRateLimitDeferral(DateTime nowUtc, DateTime retryAtUtc)
    {
        if (_lastRateLimitLogUtc.HasValue && nowUtc - _lastRateLimitLogUtc.Value < TimeSpan.FromSeconds(5))
        {
            return;
        }

        var retrySeconds = Math.Max(1, (int)Math.Ceiling((retryAtUtc - nowUtc).TotalSeconds));
        _log.Warn($"Discord RPC update deferred by local rate limit. Retrying in {retrySeconds}s.");
        _lastRateLimitLogUtc = nowUtc;
    }

    private static bool IsMissingClientId(string? clientId)
    {
        return string.IsNullOrWhiteSpace(clientId) ||
            clientId.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase);
    }

    private static bool AssetMappingsEqual(
        IReadOnlyDictionary<string, string>? left,
        IReadOnlyDictionary<string, string>? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left.Count != right.Count)
        {
            return false;
        }

        foreach (var pair in left)
        {
            if (!right.TryGetValue(pair.Key, out var rightValue) ||
                !string.Equals(pair.Value, rightValue, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
