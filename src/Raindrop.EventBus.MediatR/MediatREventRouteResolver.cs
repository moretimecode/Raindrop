using System.Reflection;
using Raindrop.EventBus.Abstractions;

namespace Raindrop.EventBus.MediatR;

/// <summary>
/// MediatR 本地事件总线的路由解析器。
/// </summary>
public sealed class MediatREventRouteResolver : IEventRouteResolver
{
    public IReadOnlyCollection<EventRoute> Resolve(Type eventType)
    {
        ArgumentNullException.ThrowIfNull(eventType);

        var attributes = eventType
            .GetCustomAttributes<EventRouteAttribute>(inherit: false)
            .ToArray();

        if (attributes.Length == 0)
        {
            return [new EventRoute(new LocalEventAttribute())];
        }

        return attributes
            .OfType<LocalEventAttribute>()
            .Select(attribute => new EventRoute(attribute))
            .ToArray();
    }
}
