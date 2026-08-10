using RabbitMQ.Client;

namespace Raindrop.EventBus.RabbitMQ;

/// <summary>
/// IModel → IRabbitMqChannel 适配器。
/// </summary>
public sealed class RabbitMqChannelAdapter : IRabbitMqChannel
{
    private readonly IModel _channel;

    public RabbitMqChannelAdapter(IModel channel)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
    }

    /// <summary>
    /// 内部 IModel，供 HostedService Consumer 回调使用。
    /// </summary>
    public IModel InnerChannel => _channel;

    public void ExchangeDeclare(string exchange, string type, bool durable, bool autoDelete)
    {
        _channel.ExchangeDeclare(exchange, type, durable, autoDelete);
    }

    public QueueDeclareOk QueueDeclare(string queue, bool durable, bool exclusive, bool autoDelete,
        IDictionary<string, object>? arguments)
    {
        return _channel.QueueDeclare(queue, durable, exclusive, autoDelete, arguments);
    }

    public void QueueBind(string queue, string exchange, string routingKey)
    {
        _channel.QueueBind(queue, exchange, routingKey);
    }

    public void BasicQos(uint prefetchSize, ushort prefetchCount, bool global)
    {
        _channel.BasicQos(prefetchSize, prefetchCount, global);
    }

    public string BasicConsume(string queue, bool autoAck, IBasicConsumer consumer)
    {
        return _channel.BasicConsume(queue, autoAck, consumer);
    }

    public void BasicCancel(string consumerTag)
    {
        _channel.BasicCancel(consumerTag);
    }

    public void Dispose()
    {
        _channel.Dispose();
    }
}
