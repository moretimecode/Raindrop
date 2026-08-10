using Raindrop.Data.Events;

namespace Raindrop.EventBus.Abstractions;

/// <summary>
/// 事件处理器契约。
/// </summary>
public interface IEventHandler<in TEvent>
    where TEvent : IEvent
{
    Task HandleAsync(TEvent eventData, CancellationToken cancellationToken = default);
}
