using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using Raindrop.Data.Events;
using Raindrop.EventBus.Abstractions;

namespace Raindrop.EventBus.RabbitMQ;

public sealed class RabbitMqEventTransport(IConnection connection) : IEventTransport
{
    private readonly IConnection _connection = connection;

    public bool CanHandle(EventRoute route)
    {
        return route.Metadata is RabbitMqEventAttribute;
    }

    public Task PublishAsync<TEvent>(
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

        using var channel = _connection.CreateModel();
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(eventData));
        var properties = channel.CreateBasicProperties();
        properties.Persistent = true;
        properties.ContentType = "application/json";
        properties.Type = typeof(TEvent).AssemblyQualifiedName;
        properties.Headers = new Dictionary<string, object>();

        channel.BasicPublish(
            rabbitMqRoute.Exchange,
            rabbitMqRoute.RoutingKey,
            mandatory: false,
            basicProperties: properties,
            body: body);

        return Task.CompletedTask;
    }
}
