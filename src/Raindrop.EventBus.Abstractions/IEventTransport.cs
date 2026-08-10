using Raindrop.Data.Events;

namespace Raindrop.EventBus.Abstractions;

/// <summary>
/// 具体事件总线的发送适配器。
/// </summary>
public interface IEventTransport
{
    bool CanHandle(EventRoute route);

    Task PublishAsync<TEvent>(
        TEvent eventData,
        EventRoute route,
        CancellationToken cancellationToken = default)
        where TEvent : IEvent;
}
