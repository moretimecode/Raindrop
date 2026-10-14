namespace Raindrop.EventBus.RabbitMQ;

public interface IRabbitMqTopologyDeclarer
{
    Task DeclareAsync(IRabbitMqChannel channel, string exchange, string routingKey, string? queue = null,
        CancellationToken cancellationToken = default);
}
