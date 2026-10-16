using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using Raindrop.EventBus.Abstractions;

namespace Raindrop.Data.EFCore;

/// <summary>
/// 当前调用中已准备的提交后发送。仅保留现行尽力发送语义，不提供 Outbox 或持久恢复。
/// </summary>
internal sealed class PreparedEventDispatch(
    ImmutableArray<IIntegrationEvent> integrationEvents,
    IEventBus? eventBus,
    ILogger<DomainEventProcessor>? logger)
{
    public async Task CompleteAsync(CancellationToken cancellationToken)
    {
        foreach (var integrationEvent in integrationEvents)
        {
            try
            {
                var publisher = eventBus ?? throw new InvalidOperationException("集成事件缺少事件总线。");
                await EventBusDispatch.PublishAsync(publisher, integrationEvent, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                UnitOfWorkDiagnostics.TryLog(() => logger?.LogError(exception, "集成事件发布失败，事件类型: {EventType}", integrationEvent.GetType().FullName));
            }
        }
    }
}
