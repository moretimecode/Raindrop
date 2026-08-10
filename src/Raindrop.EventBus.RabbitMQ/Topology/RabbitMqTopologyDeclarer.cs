using RabbitMQ.Client;

namespace Raindrop.EventBus.RabbitMQ;

public sealed class RabbitMqTopologyDeclarer : IRabbitMqTopologyDeclarer
{
    public const string DeadLetterExchange = "raindrop.dlx";
    public const string DeadLetterQueue = "raindrop.dlq";
    public const string DeadLetterRoutingKey = "raindrop.dlx";

    public void Declare(IRabbitMqChannel channel, string exchange, string routingKey, string? queue = null)
    {
        ArgumentNullException.ThrowIfNull(channel);
        if (string.IsNullOrWhiteSpace(exchange))
            throw new ArgumentException("交换机名称不能为空。", nameof(exchange));
        if (string.IsNullOrWhiteSpace(routingKey))
            throw new ArgumentException("路由键不能为空。", nameof(routingKey));

        var mainQueue = queue ?? $"raindrop:{exchange}:{routingKey}";
        var retryQueue = $"{mainQueue}:retry";

        channel.ExchangeDeclare(exchange, ExchangeType.Direct, durable: true, autoDelete: false);
        channel.ExchangeDeclare(DeadLetterExchange, ExchangeType.Direct, durable: true, autoDelete: false);

        channel.QueueDeclare(DeadLetterQueue, durable: true, exclusive: false, autoDelete: false, arguments: null);

        channel.QueueDeclare(mainQueue, durable: true, exclusive: false, autoDelete: false, new Dictionary<string, object>
        {
            ["x-dead-letter-exchange"] = DeadLetterExchange,
            ["x-dead-letter-routing-key"] = DeadLetterRoutingKey
        });

        channel.QueueDeclare(retryQueue, durable: true, exclusive: false, autoDelete: false, new Dictionary<string, object>
        {
            ["x-dead-letter-exchange"] = exchange,
            ["x-dead-letter-routing-key"] = routingKey
        });

        channel.QueueBind(mainQueue, exchange, routingKey);
        channel.QueueBind(DeadLetterQueue, DeadLetterExchange, DeadLetterRoutingKey);
    }
}
