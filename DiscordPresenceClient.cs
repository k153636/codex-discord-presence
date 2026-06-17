using DiscordRPC;

namespace CodexDiscordPresence;

public sealed class DiscordPresenceClient : IDisposable
{
    private readonly DiscordOptions _options;
    private DiscordRpcClient? _client;
    private bool _isReady;
    private DateTime _nextInitializeAttemptUtc = DateTime.MinValue;

    public DiscordPresenceClient(DiscordOptions options)
    {
        _options = options;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        TryInitialize(logSuccess: true);
        return Task.CompletedTask;
    }

    public void Update(RenderedPresence presence)
    {
        if (!EnsureReady())
        {
            return;
        }

        var client = _client;
        if (client is null)
        {
            return;
        }

        try
        {
            var buttons = presence.Buttons
                .Where(button => !string.IsNullOrWhiteSpace(button.Label) && !string.IsNullOrWhiteSpace(button.Url))
                .Select(button => new Button { Label = button.Label, Url = button.Url })
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
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Discord RPC update failed: {ex.Message}");
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
            }
            else if (!_isReady)
            {
                Console.Error.WriteLine("Discord RPC is not ready. Updates will retry.");
            }
        }
        catch (Exception ex)
        {
            _isReady = false;
            Console.Error.WriteLine($"Discord RPC initialization failed: {ex.Message}");
        }

        _nextInitializeAttemptUtc = _isReady ? DateTime.MinValue : DateTime.UtcNow.AddSeconds(30);
        return _isReady;
    }
}
