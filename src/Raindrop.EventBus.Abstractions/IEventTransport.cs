using Raindrop.Data.Events;

namespace Raindrop.EventBus.Abstractions;

/// <summary>
/// 具体事件总线的发送适配器。
/// </summary>
public interface IEventTransport
{
    bool CanHandle(EventRoute route);

    /// <summary>纯粹冻结发送地址，禁止创建连接或发送消息；未支持时明确拒绝可靠登记。</summary>
    EventDeliveryTarget CaptureTarget(EventRoute route)
        => throw new NotSupportedException("当前 transport 不支持 Outbox 发送目标快照。");

    Task PublishAsync<TEvent>(
        TEvent eventData,
        EventRoute route,
        CancellationToken cancellationToken = default)
        where TEvent : IEvent;
}
