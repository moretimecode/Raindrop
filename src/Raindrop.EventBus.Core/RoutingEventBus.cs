using Raindrop.Data.Events;
using Raindrop.EventBus.Abstractions;

namespace Raindrop.EventBus.Core;

public class RoutingEventBus : IEventBus
{
    private readonly IEnumerable<IEventRouteResolver> _routeResolvers;
    private readonly IEnumerable<IEventTransport> _transports;
    private readonly IEventOutbox? _outbox;

    public RoutingEventBus(
        IEnumerable<IEventRouteResolver> routeResolvers,
        IEnumerable<IEventTransport> transports,
        IEventOutbox? outbox = null)
    {
        _routeResolvers = routeResolvers;
        _transports = transports;
        _outbox = outbox;
    }

    public async Task PublishAsync<TEvent>(
        TEvent eventData,
        CancellationToken cancellationToken = default)
        where TEvent : IEvent
    {
        ArgumentNullException.ThrowIfNull(eventData);

        if (eventData is IIntegrationEvent integrationEvent && _outbox is not null)
        {
            await _outbox.EnqueueAsync(integrationEvent, cancellationToken).ConfigureAwait(false);
            return;
        }

        var eventType = eventData.GetType();
        var routes = _routeResolvers
            .SelectMany(resolver => resolver.Resolve(eventType))
            .ToArray();

        if (routes.Length == 0)
        {
            throw new InvalidOperationException($"事件 {eventType.Name} 没有解析到任何事件总线路由。");
        }

        if (_outbox is not null && routes.Any(route => route.Metadata is not LocalEventAttribute))
            throw new InvalidOperationException("启用 Outbox 后，普通领域事件只能本地处理；远程事件必须实现 IIntegrationEvent。");

        foreach (var route in routes)
        {
            var handled = false;

            foreach (var transport in _transports.Where(transport => transport.CanHandle(route)))
            {
                handled = true;
                await transport.PublishAsync(eventData, route, cancellationToken);
            }

            if (!handled)
            {
                throw new InvalidOperationException(
                    $"没有找到可以处理 {route.Metadata.GetType().Name} 的事件总线适配器。");
            }
        }
    }
}
