namespace Raindrop.EventBus.RabbitMQ;

public interface IRabbitMqReceivedMessage
{
    int FailedCount { get; }

    Task AckAsync(CancellationToken cancellationToken = default);

    Task RetryAsync(TimeSpan delay, CancellationToken cancellationToken = default);

    Task DeadLetterAsync(CancellationToken cancellationToken = default);
}
