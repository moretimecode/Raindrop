using Raindrop.Data.Events;

namespace Raindrop.EventBus.Abstractions;

/// <summary>把集成事件登记为当前本地事务中的发送意图；返回不表示已提交或已交付。</summary>
public interface IEventOutbox
{
    Task EnqueueAsync(IIntegrationEvent eventData, CancellationToken cancellationToken = default);
}
