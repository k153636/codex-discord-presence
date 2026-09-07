using DiscordRPC;

namespace CodexDiscordPresence;

public sealed class DiscordPresenceClient : IDisposable
{
    private DiscordOptions _options;
    private readonly DiagnosticLog _log;
    private DiscordRpcClient? _client;
    private bool _isReady;
    private bool _needsPresenceRefresh = true;
    private DateTime _nextInitializeAttemptUtc = DateTime.MinValue;
    private int _failedInitializeAttempts;
    private readonly string _partyId = $"codex-party-{Guid.NewGuid():N}";

    public DiscordPresenceClient(DiscordOptions options, DiagnosticLog log)
    {
        _options = options;
        _log = log;
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
            _failedInitializeAttempts = 0;
            _nextInitializeAttemptUtc = DateTime.MinValue;
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
            var publishedPresence = DiscordRichPresenceBuilder.Create(_options, presence, _partyId);
            client.SetPresence(publishedPresence);
            LastPublishedPresence = DiscordPresenceSnapshot.From(publishedPresence);
            _needsPresenceRefresh = false;
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
            _nextInitializeAttemptUtc = DateTime.UtcNow.Add(delay);
            return false;
        }
    }

    public void Clear()
    {
        try
        {
            _client?.ClearPresence();
        }
        catch (Exception ex)
        {
            _log.Error("Discord RPC clear failed", ex);
        }
        finally
        {
            LastPublishedPresence = null;
            _needsPresenceRefresh = true;
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
            _needsPresenceRefresh = true;
        }
    }

    private bool EnsureReady()
    {
        if (!_isReady && DateTime.UtcNow < _nextInitializeAttemptUtc)
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
                _nextInitializeAttemptUtc = DateTime.UtcNow.Add(delay);
                return false;
            }

            ResetClient();
            _client = new DiscordRpcClient(_options.ClientId);
            _isReady = _client.Initialize();

            if (_isReady)
            {
                _failedInitializeAttempts = 0;
                LastPublishedPresence = null;
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
            _needsPresenceRefresh = true;
            _failedInitializeAttempts = Math.Min(_failedInitializeAttempts + 1, int.MaxValue);
            var delay = DiscordReconnectBackoff.GetDelay(_failedInitializeAttempts);
            _log.Error($"Discord RPC initialization failed. Reconnecting in {delay.TotalSeconds:0}s.", ex);
        }

        _nextInitializeAttemptUtc = _isReady
            ? DateTime.MinValue
            : DateTime.UtcNow.Add(DiscordReconnectBackoff.GetDelay(_failedInitializeAttempts));
        return _isReady;
    }

    private void ResetClient()
    {
        _client?.Dispose();
        _client = null;
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
