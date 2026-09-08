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
        TryInitialize(logSuccess: true);
        return Task.CompletedTask;
    }

    public bool NeedsPresenceRefresh => _needsPresenceRefresh;

    public bool IsConnected => _isReady;

    public DiscordPresenceSnapshot? LastPublishedPresence { get; private set; }

    public void UpdateOptions(DiscordOptions options)
    {
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

            var publishedPresence = DiscordRichPresenceBuilder.Create(_options, presence, _partyId);
            var nowUtc = _utcNow();
            if (!_updateThrottle.TryReserve(nowUtc, out var retryAtUtc))
            {
                LogRateLimitDeferral(nowUtc, retryAtUtc);
                return false;
            }

            client.SetPresence(publishedPresence);
            LastPublishedPresence = DiscordPresenceSnapshot.From(publishedPresence);
            _clearSentForCurrentConnection = false;
            _needsPresenceRefresh = false;
            _lastRateLimitLogUtc = null;
            return true;
        }
        catch (Exception ex)
        {
            _isReady = false;
            ResetClient();
            LastPublishedPresence = null;
            _needsPresenceRefresh = true;
            _failedInitializeAttempts = Math.Min(_failedInitializeAttempts + 1, int.MaxValue);
            var delay = DiscordReconnectBackoff.GetDelay(_failedInitializeAttempts);
            _log.Error($"Discord RPC update failed. Reconnecting in {delay.TotalSeconds:0}s.", ex);
            _nextInitializeAttemptUtc = _utcNow().Add(delay);
            return false;
        }
    }

    public void Clear()
    {
        if (_client is null)
        {
            LastPublishedPresence = null;
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
            _client.ClearPresence();
            _clearPending = false;
            _clearSentForCurrentConnection = true;
            LastPublishedPresence = null;
            _needsPresenceRefresh = true;
            _lastRateLimitLogUtc = null;
        }
        catch (Exception ex)
        {
            _isReady = false;
            ResetClient();
            _clearPending = true;
            _clearSentForCurrentConnection = false;
            LastPublishedPresence = null;
            _needsPresenceRefresh = true;
            _failedInitializeAttempts = Math.Min(_failedInitializeAttempts + 1, int.MaxValue);
            var delay = DiscordReconnectBackoff.GetDelay(_failedInitializeAttempts);
            _nextInitializeAttemptUtc = _utcNow().Add(delay);
            _log.Error($"Discord RPC clear failed. Reconnecting in {delay.TotalSeconds:0}s.", ex);
        }
    }

    public void Dispose()
    {
        try
        {
            _client?.Dispose();
        }
        finally
        {
            _client = null;
            _isReady = false;
            LastPublishedPresence = null;
            _clearPending = false;
            _clearSentForCurrentConnection = false;
            _needsPresenceRefresh = true;
        }
    }

    private bool EnsureReady()
    {
        if (!_isReady && _utcNow() < _nextInitializeAttemptUtc)
        {
            return false;
        }

        return _isReady || TryInitialize(logSuccess: false);
    }

    private bool TryInitialize(bool logSuccess)
    {
        try
        {
            if (IsMissingClientId(_options.ClientId))
            {
                _isReady = false;
                LastPublishedPresence = null;
                _needsPresenceRefresh = true;
                _failedInitializeAttempts = Math.Min(_failedInitializeAttempts + 1, int.MaxValue);
                var delay = DiscordReconnectBackoff.GetDelay(_failedInitializeAttempts);
                _log.Warn($"Discord RPC client id is not configured for the current profile. Reconnecting in {delay.TotalSeconds:0}s.");
                _nextInitializeAttemptUtc = _utcNow().Add(delay);
                return false;
            }

            ResetClient();
            _client = _transportFactory(_options.ClientId);
            _isReady = _client.Initialize();

            if (_isReady)
            {
                _failedInitializeAttempts = 0;
                LastPublishedPresence = null;
                _clearSentForCurrentConnection = false;
                _needsPresenceRefresh = true;
                if (logSuccess)
                {
                    _log.Info("Discord RPC initialized.");
                }
            }
            else if (!_isReady)
            {
                ResetClient();
                LastPublishedPresence = null;
                _clearSentForCurrentConnection = false;
                _failedInitializeAttempts = Math.Min(_failedInitializeAttempts + 1, int.MaxValue);
                var delay = DiscordReconnectBackoff.GetDelay(_failedInitializeAttempts);
                _log.Warn($"Discord RPC is not ready. Reconnecting in {delay.TotalSeconds:0}s.");
                _needsPresenceRefresh = true;
            }
        }
        catch (Exception ex)
        {
            _isReady = false;
            ResetClient();
            LastPublishedPresence = null;
            _clearSentForCurrentConnection = false;
            _needsPresenceRefresh = true;
            _failedInitializeAttempts = Math.Min(_failedInitializeAttempts + 1, int.MaxValue);
            var delay = DiscordReconnectBackoff.GetDelay(_failedInitializeAttempts);
            _log.Error($"Discord RPC initialization failed. Reconnecting in {delay.TotalSeconds:0}s.", ex);
        }

        _nextInitializeAttemptUtc = _isReady
            ? DateTime.MinValue
            : _utcNow().Add(DiscordReconnectBackoff.GetDelay(_failedInitializeAttempts));
        return _isReady;
    }

    private void ResetClient()
    {
        _client?.Dispose();
        _client = null;
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
