using System.Reflection;
using Raindrop.EventBus.Abstractions;

namespace Raindrop.EventBus.RabbitMQ;

/// <summary>
/// RabbitMQ 事件总线的路由解析器。
/// </summary>
public sealed class RabbitMqEventRouteResolver : IEventRouteResolver
{
    public IReadOnlyCollection<EventRoute> Resolve(Type eventType)
    {
        ArgumentNullException.ThrowIfNull(eventType);

        return eventType
            .GetCustomAttributes<RabbitMqEventAttribute>(inherit: false)
            .Select(attribute => new EventRoute(attribute))
            .ToArray();
    }
}
