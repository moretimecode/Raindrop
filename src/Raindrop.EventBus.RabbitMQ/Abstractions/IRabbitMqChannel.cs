using RabbitMQ.Client;

namespace Raindrop.EventBus.RabbitMQ;

/// <summary>
/// 对 RabbitMQ 异步通道的薄封装，方便单元测试替换。
/// </summary>
public interface IRabbitMqChannel : IDisposable, IAsyncDisposable
{
    Task ExchangeDeclareAsync(string exchange, string type, bool durable, bool autoDelete,
        CancellationToken cancellationToken = default);

    Task<QueueDeclareOk> QueueDeclareAsync(string queue, bool durable, bool exclusive, bool autoDelete,
        IDictionary<string, object?>? arguments, CancellationToken cancellationToken = default);

    Task QueueBindAsync(string queue, string exchange, string routingKey,
        CancellationToken cancellationToken = default);

    Task BasicQosAsync(uint prefetchSize, ushort prefetchCount, bool global,
        CancellationToken cancellationToken = default);

    Task<string> BasicConsumeAsync(string queue, bool autoAck, IAsyncBasicConsumer consumer,
        CancellationToken cancellationToken = default);

    Task BasicCancelAsync(string consumerTag, CancellationToken cancellationToken = default);

    ValueTask BasicPublishAsync(string exchange, string routingKey, bool mandatory,
        BasicProperties properties, ReadOnlyMemory<byte> body, CancellationToken cancellationToken = default);
}
