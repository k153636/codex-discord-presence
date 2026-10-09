namespace CodexDiscordPresence;

internal sealed class ApplicationUpdateRetryException(string? message, DateTime retryAfterUtc)
    : InvalidOperationException($"{message} Retry after {retryAfterUtc:u}.")
{
    public DateTime RetryAfterUtc { get; } = retryAfterUtc;
}
