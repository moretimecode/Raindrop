using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Raindrop.Data.Entities;
using Raindrop.Data.Events;
using Raindrop.EventBus.Abstractions;

namespace Raindrop.Data.EFCore;

/// <summary>
/// 领域事件处理器。
/// 递归地从 ChangeTracker 中收集并处理领域事件，集成事件由调用方在事务提交后发布。
/// </summary>
public sealed class DomainEventProcessor
{
    private readonly IEventBus _eventBus;
    private readonly ILogger<DomainEventProcessor>? _logger;

    public DomainEventProcessor(
        IEventBus eventBus,
        ILogger<DomainEventProcessor>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(eventBus);
        _eventBus = eventBus;
        _logger = logger;
    }

    /// <summary>
    /// 递归处理领域事件。
    /// 返回本轮及递归过程中产生的所有集成事件（IIntegrationEvent），
    /// 由调用方在 SaveChanges / 事务提交之后发布。
    /// </summary>
    public async Task<List<IIntegrationEvent>> ProcessAsync(DbContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var allIntegrationEvents = new List<IIntegrationEvent>();
        await ProcessRecursiveAsync(context, allIntegrationEvents, cancellationToken);
        return allIntegrationEvents;
    }

    private async Task ProcessRecursiveAsync(
        DbContext context,
        List<IIntegrationEvent> allIntegrationEvents,
        CancellationToken cancellationToken)
    {
        var events = CollectDomainEvents(context, allIntegrationEvents);

        if (events.Count == 0)
        {
            return;
        }

        foreach (var domainEvent in events)
        {
            // 领域事件：异常必须向上传播，让 CommitAsync 失败
            // 领域事件 handler 失败 → 业务规则被破坏 → 不应继续 SaveChanges
            await PublishDomainEventAsync(domainEvent, cancellationToken);
        }

        // 递归：事件处理程序可能修改了其他聚合根，产生新事件
        await ProcessRecursiveAsync(context, allIntegrationEvents, cancellationToken);
    }

    private static List<IEvent> CollectDomainEvents(DbContext context, List<IIntegrationEvent> allIntegrationEvents)
    {
        var events = new List<IEvent>();

        var entries = context.ChangeTracker
            .Entries<IEntity>()
            .Where(e => e.Entity.GetDomainEvents().Count > 0)
            .ToArray();

        foreach (var entry in entries)
        {
            foreach (var domainEvent in entry.Entity.GetDomainEvents())
            {
                if (domainEvent is IIntegrationEvent integrationEvent)
                {
                    allIntegrationEvents.Add(integrationEvent);
                }
                else
                {
                    events.Add(domainEvent);
                }
            }

            entry.Entity.ClearDomainEvents();
        }

        return events;
    }

    private async Task PublishDomainEventAsync(IEvent @event, CancellationToken cancellationToken)
    {
        // 领域事件异常不捕获——让调用方决定是否回滚
        await PublishViaReflectionAsync(@event, cancellationToken);
    }

    /// <summary>
    /// 发布集成事件（事务提交后调用）。
    /// 集成事件发布失败不应回滚已提交的事务。
    /// </summary>
    public async Task PublishIntegrationEventsAsync(
        IEnumerable<IIntegrationEvent> integrationEvents,
        CancellationToken cancellationToken = default)
    {
        foreach (var integrationEvent in integrationEvents)
        {
            try
            {
                await PublishViaReflectionAsync(integrationEvent, cancellationToken);
            }
            catch (Exception ex)
            {
                // 集成事件发布失败不应影响已提交的事务。
                // 如需严格交付保证，请结合 Outbox 模式使用。
                _logger?.LogError(
                    ex,
                    "集成事件发布失败，事件类型: {EventType}",
                    integrationEvent.GetType().FullName);
            }
        }
    }

    private async Task PublishViaReflectionAsync(IEvent @event, CancellationToken cancellationToken)
    {
        var publishMethod = typeof(IEventBus)
            .GetMethod(nameof(IEventBus.PublishAsync), BindingFlags.Public | BindingFlags.Instance)!;

        var genericMethod = publishMethod.MakeGenericMethod(@event.GetType());

        if (genericMethod.Invoke(_eventBus, [@event, cancellationToken]) is Task task)
        {
            await task.ConfigureAwait(false);
        }
    }
}
