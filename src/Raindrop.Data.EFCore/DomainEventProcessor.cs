using System.Collections.Immutable;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Raindrop.Data.Entities;
using Raindrop.Data.Events;
using Raindrop.EventBus.Abstractions;

namespace Raindrop.Data.EFCore;

/// <summary>
/// 准备当前工作单元的事件：迭代执行领域事件，集成事件登记 Outbox 或保留旧的提交后发送。
/// 纯批次分类与取出实体事件、调用 handler 等副作用分开。
/// </summary>
public sealed class DomainEventProcessor(
    IEventBus? eventBus = null,
    ILogger<DomainEventProcessor>? logger = null,
    IEventOutbox? outbox = null)
{
    private const int MaxProcessingRounds = 1024;

    internal bool UsesOutbox => outbox is not null;

    internal async Task<PreparedEventDispatch> PrepareAsync(DbContext context, CancellationToken cancellationToken)
    {
        var integrationEvents = ImmutableArray.CreateBuilder<IIntegrationEvent>();
        var rounds = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var events = TakePendingEvents(context);
            if (events.IsEmpty)
            {
                if (outbox is null)
                    return new PreparedEventDispatch(integrationEvents.ToImmutable(), eventBus, logger);
                foreach (var integrationEvent in integrationEvents)
                    await outbox.EnqueueAsync(integrationEvent, cancellationToken).ConfigureAwait(false);
                return new PreparedEventDispatch([], eventBus, logger);
            }

            if (++rounds > MaxProcessingRounds)
                throw new InvalidOperationException($"领域事件处理超过 {MaxProcessingRounds} 轮，请检查事件循环。");

            var publisher = eventBus ?? throw new InvalidOperationException("当前工作单元包含事件，但没有注册 IEventBus。");
            var batch = DomainEventBatch.Classify(events);
            integrationEvents.AddRange(batch.IntegrationEvents);

            foreach (var domainEvent in batch.DomainEvents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await EventBusDispatch.PublishAsync(publisher, domainEvent, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>取出并清空当前实体事件；失败后必须丢弃当前工作单元，不能恢复内存执行现场。</summary>
    private static ImmutableArray<IEvent> TakePendingEvents(DbContext context)
    {
        var entities = context.ChangeTracker.Entries<IEntity>()
            .Select(entry => entry.Entity)
            .Where(entity => entity.GetDomainEvents().Count > 0)
            .ToArray();
        var events = entities.SelectMany(entity => entity.GetDomainEvents()).ToImmutableArray();

        foreach (var entity in entities)
            entity.ClearDomainEvents();

        return events;
    }
}
