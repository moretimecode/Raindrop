using Raindrop.Data.Events;

namespace Raindrop.EventBus.Abstractions;

/// <summary>
/// 统一事件发布入口。具体进入哪些事件总线，由事件路由元数据决定。
/// </summary>
public interface IEventBus
{
    Task PublishAsync<TEvent>(TEvent eventData, CancellationToken cancellationToken = default)
        where TEvent : IEvent;
}
