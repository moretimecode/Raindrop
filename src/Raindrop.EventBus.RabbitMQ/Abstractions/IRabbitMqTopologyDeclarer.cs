using Raindrop.EventBus.Abstractions;

namespace Raindrop.EventBus.RabbitMQ;

public interface IRabbitMqTopologyDeclarer
{
    void Declare(IRabbitMqChannel channel, string exchange, string routingKey, string? queue = null);
}
