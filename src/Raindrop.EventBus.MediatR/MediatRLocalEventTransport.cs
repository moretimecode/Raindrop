using MediatR;
using Raindrop.Data.Events;
using Raindrop.EventBus.Abstractions;

namespace Raindrop.EventBus.MediatR;

public class MediatRLocalEventTransport(IPublisher publisher) : IEventTransport
{
    private readonly IPublisher _publisher = publisher;

    public bool CanHandle(EventRoute route)
    {
        return route.Metadata is LocalEventAttribute;
    }

    public Task PublishAsync<TEvent>(
        TEvent eventData,
        EventRoute route,
        CancellationToken cancellationToken = default)
        where TEvent : IEvent
    {
        return _publisher.Publish(
            new MediatREventNotification<TEvent>(eventData),
            cancellationToken);
    }
}
