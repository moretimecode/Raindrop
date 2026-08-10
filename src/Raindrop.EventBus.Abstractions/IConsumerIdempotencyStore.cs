namespace Raindrop.EventBus.Abstractions;

public interface IConsumerIdempotencyStore
{
    Task<ConsumerIdempotencyBeginResult> TryBeginAsync(
        string key,
        TimeSpan expiration,
        CancellationToken cancellationToken = default);

    Task MarkSucceededAsync(
        string key,
        CancellationToken cancellationToken = default);

    Task MarkFailedAsync(
        string key,
        CancellationToken cancellationToken = default);
}
