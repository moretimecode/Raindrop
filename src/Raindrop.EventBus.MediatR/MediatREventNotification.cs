using MediatR;
using Raindrop.Data.Events;

namespace Raindrop.EventBus.MediatR;

public sealed class MediatREventNotification<TEvent>(TEvent eventData) : INotification
    where TEvent : IEvent
{

    public TEvent EventData { get; } = eventData;
}
