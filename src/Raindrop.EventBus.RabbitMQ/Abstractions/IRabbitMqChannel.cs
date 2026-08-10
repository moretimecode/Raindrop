using RabbitMQ.Client;

namespace Raindrop.EventBus.RabbitMQ;

/// <summary>
/// 对 RabbitMQ IModel 的薄封装，方便单元测试 mock。
/// </summary>
public interface IRabbitMqChannel : IDisposable
{
    void ExchangeDeclare(string exchange, string type, bool durable, bool autoDelete);

    QueueDeclareOk QueueDeclare(string queue, bool durable, bool exclusive, bool autoDelete,
        IDictionary<string, object>? arguments);

    void QueueBind(string queue, string exchange, string routingKey);

    void BasicQos(uint prefetchSize, ushort prefetchCount, bool global);

    string BasicConsume(string queue, bool autoAck, IBasicConsumer consumer);

    void BasicCancel(string consumerTag);
}
