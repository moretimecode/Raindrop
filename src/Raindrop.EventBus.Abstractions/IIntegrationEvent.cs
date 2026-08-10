using Raindrop.Data.Events;

namespace Raindrop.EventBus.Abstractions;

/// <summary>
/// 用于跨进程或跨服务传递的集成事件。
/// </summary>
public interface IIntegrationEvent : IEvent
{
}
