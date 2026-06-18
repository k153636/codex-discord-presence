using DiscordRPC;
using RpcButton = DiscordRPC.Button;

namespace CodexDiscordPresence;

public sealed class DiscordPresenceClient : IDisposable
{
    private readonly DiscordOptions _options;
    private DiscordRpcClient? _client;
    private bool _isReady;
    private DateTime _nextInitializeAttemptUtc = DateTime.MinValue;
    private int _failedInitializeAttempts;

    public DiscordPresenceClient(DiscordOptions options)
    {
        _options = options;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        TryInitialize(logSuccess: true);
        return Task.CompletedTask;
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
            var buttons = presence.Buttons
                .Where(button => !string.IsNullOrWhiteSpace(button.Label) && !string.IsNullOrWhiteSpace(button.Url))
                .Select(button => new RpcButton { Label = button.Label, Url = button.Url })
                .Take(2)
                .ToArray();

            client.SetPresence(new RichPresence
            {
                Details = presence.Details,
                State = presence.State,
                Assets = new Assets
                {
                    LargeImageKey = _options.LargeImageKey,
                    LargeImageText = presence.LargeImageText,
                    SmallImageKey = _options.SmallImageKey,
                    SmallImageText = presence.SmallImageText
                },
                Buttons = buttons.Length == 0 ? null : buttons,
                Timestamps = presence.StartedAt is null ? null : new Timestamps(presence.StartedAt.Value)
            });
            return true;
        }
        catch (Exception ex)
        {
            _isReady = false;
            _failedInitializeAttempts = Math.Min(_failedInitializeAttempts + 1, int.MaxValue);
            var delay = DiscordReconnectBackoff.GetDelay(_failedInitializeAttempts);
            Console.Error.WriteLine($"Discord RPC update failed: {ex.Message}. Reconnecting in {delay.TotalSeconds:0}s.");
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
            Console.Error.WriteLine($"Discord RPC clear failed: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _client?.Dispose();
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
            _client?.Dispose();
            _client = new DiscordRpcClient(_options.ClientId);
            _isReady = _client.Initialize();

            if (_isReady && logSuccess)
            {
                Console.WriteLine("Discord RPC initialized.");
                _failedInitializeAttempts = 0;
            }
            else if (!_isReady)
            {
                _failedInitializeAttempts = Math.Min(_failedInitializeAttempts + 1, int.MaxValue);
                var delay = DiscordReconnectBackoff.GetDelay(_failedInitializeAttempts);
                Console.Error.WriteLine($"Discord RPC is not ready. Reconnecting in {delay.TotalSeconds:0}s.");
            }
        }
        catch (Exception ex)
        {
            _isReady = false;
            _failedInitializeAttempts = Math.Min(_failedInitializeAttempts + 1, int.MaxValue);
            var delay = DiscordReconnectBackoff.GetDelay(_failedInitializeAttempts);
            Console.Error.WriteLine($"Discord RPC initialization failed: {ex.Message}. Reconnecting in {delay.TotalSeconds:0}s.");
        }

        _nextInitializeAttemptUtc = _isReady
            ? DateTime.MinValue
            : DateTime.UtcNow.Add(DiscordReconnectBackoff.GetDelay(_failedInitializeAttempts));
        return _isReady;
    }
}
