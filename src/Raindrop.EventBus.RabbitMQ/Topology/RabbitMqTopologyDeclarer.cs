using RabbitMQ.Client;

namespace Raindrop.EventBus.RabbitMQ;

public sealed class RabbitMqTopologyDeclarer : IRabbitMqTopologyDeclarer
{
    public const string DeadLetterExchange = "raindrop.dlx";
    public const string DeadLetterQueue = "raindrop.dlq";
    public const string DeadLetterRoutingKey = "raindrop.dlx";

    public async Task DeclareAsync(IRabbitMqChannel channel, string exchange, string routingKey, string? queue = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channel);
        if (string.IsNullOrWhiteSpace(exchange))
            throw new ArgumentException("交换机名称不能为空。", nameof(exchange));
        if (string.IsNullOrWhiteSpace(routingKey))
            throw new ArgumentException("路由键不能为空。", nameof(routingKey));

        var mainQueue = queue ?? $"raindrop:{exchange}:{routingKey}";
        var retryQueue = $"{mainQueue}:retry";

        await channel.ExchangeDeclareAsync(exchange, ExchangeType.Direct, durable: true, autoDelete: false, cancellationToken);
        await channel.ExchangeDeclareAsync(DeadLetterExchange, ExchangeType.Direct, durable: true, autoDelete: false, cancellationToken);

        await channel.QueueDeclareAsync(DeadLetterQueue, durable: true, exclusive: false, autoDelete: false, arguments: null, cancellationToken);

        await channel.QueueDeclareAsync(mainQueue, durable: true, exclusive: false, autoDelete: false, new Dictionary<string, object?>
        {
            ["x-dead-letter-exchange"] = DeadLetterExchange,
            ["x-dead-letter-routing-key"] = DeadLetterRoutingKey
        }, cancellationToken);

        await channel.QueueDeclareAsync(retryQueue, durable: true, exclusive: false, autoDelete: false, new Dictionary<string, object?>
        {
            ["x-dead-letter-exchange"] = exchange,
            ["x-dead-letter-routing-key"] = routingKey
        }, cancellationToken);

        await channel.QueueBindAsync(mainQueue, exchange, routingKey, cancellationToken);
        await channel.QueueBindAsync(DeadLetterQueue, DeadLetterExchange, DeadLetterRoutingKey, cancellationToken);
    }
}
