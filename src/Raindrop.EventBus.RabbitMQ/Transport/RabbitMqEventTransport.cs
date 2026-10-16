using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using Raindrop.Data.Events;
using Raindrop.EventBus.Abstractions;

namespace Raindrop.EventBus.RabbitMQ;

public sealed class RabbitMqEventTransport(IRabbitMqConnection connection) : IEventTransport
{
    private readonly IRabbitMqConnection _connection = connection;

    public bool CanHandle(EventRoute route)
    {
        return route.Metadata is RabbitMqEventAttribute;
    }

    public EventDeliveryTarget CaptureTarget(EventRoute route)
    {
        ArgumentNullException.ThrowIfNull(route);
        if (route.Metadata is not RabbitMqEventAttribute target)
            throw new InvalidOperationException("RabbitMQ 不能捕获此路由的发送目标。");
        return new EventDeliveryTarget("rabbitmq", "1", JsonSerializer.Serialize(new
        {
            exchange = target.Exchange,
            routingKey = target.RoutingKey
        }));
    }

    public async Task PublishAsync<TEvent>(
        TEvent eventData,
        EventRoute route,
        CancellationToken cancellationToken = default)
        where TEvent : IEvent
    {
        ArgumentNullException.ThrowIfNull(eventData);
        ArgumentNullException.ThrowIfNull(route);

        if (route.Metadata is not RabbitMqEventAttribute rabbitMqRoute)
        {
            throw new InvalidOperationException($"RabbitMQ 事件总线不能处理 {route.Metadata.GetType().Name} 路由。");
        }

        await using var channel = await _connection.CreateChannelAsync(cancellationToken);
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(eventData));
        var properties = new BasicProperties();
        properties.Persistent = true;
        properties.ContentType = "application/json";
        properties.Type = typeof(TEvent).AssemblyQualifiedName;
        properties.Headers = new Dictionary<string, object?>();

        await channel.BasicPublishAsync(
            rabbitMqRoute.Exchange,
            rabbitMqRoute.RoutingKey,
            mandatory: false,
            properties: properties,
            body: body,
            cancellationToken: cancellationToken);
    }
}
