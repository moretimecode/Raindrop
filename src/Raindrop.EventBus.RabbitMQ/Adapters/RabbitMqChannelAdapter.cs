using RabbitMQ.Client;

namespace Raindrop.EventBus.RabbitMQ;

/// <summary>
/// IChannel → IRabbitMqChannel 异步适配器。
/// </summary>
public sealed class RabbitMqChannelAdapter : IRabbitMqChannel
{
    private readonly IChannel _channel;

    public RabbitMqChannelAdapter(IChannel channel)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
    }

    /// <summary>
    /// 内部通道，供 HostedService 消费回调使用。
    /// </summary>
    public IChannel InnerChannel => _channel;

    public Task ExchangeDeclareAsync(string exchange, string type, bool durable, bool autoDelete,
        CancellationToken cancellationToken = default)
    {
        return _channel.ExchangeDeclareAsync(exchange, type, durable, autoDelete,
            cancellationToken: cancellationToken);
    }

    public Task<QueueDeclareOk> QueueDeclareAsync(string queue, bool durable, bool exclusive, bool autoDelete,
        IDictionary<string, object?>? arguments, CancellationToken cancellationToken = default)
    {
        return _channel.QueueDeclareAsync(queue, durable, exclusive, autoDelete, arguments,
            cancellationToken: cancellationToken);
    }

    public Task QueueBindAsync(string queue, string exchange, string routingKey,
        CancellationToken cancellationToken = default)
    {
        return _channel.QueueBindAsync(queue, exchange, routingKey, cancellationToken: cancellationToken);
    }

    public Task BasicQosAsync(uint prefetchSize, ushort prefetchCount, bool global,
        CancellationToken cancellationToken = default)
    {
        return _channel.BasicQosAsync(prefetchSize, prefetchCount, global, cancellationToken);
    }

    public Task<string> BasicConsumeAsync(string queue, bool autoAck, IAsyncBasicConsumer consumer,
        CancellationToken cancellationToken = default)
    {
        return _channel.BasicConsumeAsync(queue, autoAck, consumer, cancellationToken);
    }

    public Task BasicCancelAsync(string consumerTag, CancellationToken cancellationToken = default)
    {
        return _channel.BasicCancelAsync(consumerTag, cancellationToken: cancellationToken);
    }

    public ValueTask BasicPublishAsync(string exchange, string routingKey, bool mandatory,
        BasicProperties properties, ReadOnlyMemory<byte> body, CancellationToken cancellationToken = default)
    {
        return _channel.BasicPublishAsync(exchange, routingKey, mandatory, properties, body, cancellationToken);
    }

    public void Dispose()
    {
        _channel.Dispose();
    }

    public ValueTask DisposeAsync() => _channel.DisposeAsync();
}
