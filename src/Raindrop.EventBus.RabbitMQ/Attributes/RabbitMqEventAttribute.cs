using Raindrop.EventBus.Abstractions;

namespace Raindrop.EventBus.RabbitMQ;

/// <summary>
/// 声明事件需要投递到 RabbitMQ。
/// </summary>
public sealed class RabbitMqEventAttribute : EventRouteAttribute
{
    public RabbitMqEventAttribute(string exchange, string routingKey)
    {
        if (string.IsNullOrWhiteSpace(exchange))
        {
            throw new ArgumentException("RabbitMQ 交换机名称不能为空。", nameof(exchange));
        }

        if (string.IsNullOrWhiteSpace(routingKey))
        {
            throw new ArgumentException("RabbitMQ 路由键不能为空。", nameof(routingKey));
        }

        Exchange = exchange;
        RoutingKey = routingKey;
    }

    public string Exchange { get; }

    public string RoutingKey { get; }

    public string? Queue { get; init; }
}
