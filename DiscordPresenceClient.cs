using DiscordRPC;

namespace CodexDiscordPresence;

public sealed class DiscordPresenceClient : IDisposable
{
    private readonly DiscordOptions _options;
    private DiscordRpcClient? _client;

    public DiscordPresenceClient(DiscordOptions options)
    {
        _options = options;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _client = new DiscordRpcClient(_options.ClientId);
        _client.Initialize();
        return Task.CompletedTask;
    }

    public void Update(RenderedPresence presence)
    {
        if (_client is null)
        {
            return;
        }

        var buttons = presence.Buttons
            .Where(button => !string.IsNullOrWhiteSpace(button.Label) && !string.IsNullOrWhiteSpace(button.Url))
            .Select(button => new Button { Label = button.Label, Url = button.Url })
            .Take(2)
            .ToArray();

        _client.SetPresence(new RichPresence
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

    public void Clear()
    {
        _client?.ClearPresence();
    }

    public void Dispose()
    {
        _client?.Dispose();
    }
}
