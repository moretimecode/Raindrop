using MediatR;
using Raindrop.Data.Events;
using Raindrop.EventBus.Abstractions;

namespace Raindrop.EventBus.MediatR;

public class MediatREventNotificationHandler<TEvent>(IEnumerable<IEventHandler<TEvent>> handlers) : INotificationHandler<MediatREventNotification<TEvent>>
    where TEvent : IEvent
{
    private readonly IEnumerable<IEventHandler<TEvent>> _handlers = handlers;

    public async Task Handle(
        MediatREventNotification<TEvent> notification,
        CancellationToken cancellationToken)
    {
        foreach (var handler in _handlers)
        {
            await handler.HandleAsync(notification.EventData, cancellationToken);
        }
    }
}
