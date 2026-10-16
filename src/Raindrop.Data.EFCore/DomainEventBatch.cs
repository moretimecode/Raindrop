using System.Collections.Immutable;
using Raindrop.Data.Events;
using Raindrop.EventBus.Abstractions;

namespace Raindrop.Data.EFCore;

/// <summary>一次事件快照的纯分类结果，分别保持两类事件的原始顺序。</summary>
internal sealed record DomainEventBatch(
    ImmutableArray<IEvent> DomainEvents,
    ImmutableArray<IIntegrationEvent> IntegrationEvents)
{
    public static DomainEventBatch Classify(IEnumerable<IEvent> events)
    {
        var domainEvents = ImmutableArray.CreateBuilder<IEvent>();
        var integrationEvents = ImmutableArray.CreateBuilder<IIntegrationEvent>();

        foreach (var @event in events)
        {
            if (@event is IIntegrationEvent integrationEvent)
                integrationEvents.Add(integrationEvent);
            else
                domainEvents.Add(@event);
        }

        return new DomainEventBatch(domainEvents.ToImmutable(), integrationEvents.ToImmutable());
    }
}
