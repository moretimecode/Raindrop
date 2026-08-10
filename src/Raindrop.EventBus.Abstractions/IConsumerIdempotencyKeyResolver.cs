using Raindrop.Data.Events;

namespace Raindrop.EventBus.Abstractions;

public interface IConsumerIdempotencyKeyResolver
{
    string Resolve<TEvent>(
        TEvent eventData,
        Type handlerType,
        IdempotentConsumerAttribute attribute)
        where TEvent : IEvent;
}
