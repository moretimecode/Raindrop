namespace Raindrop.EventBus.Abstractions;

/// <summary>
/// 根据事件类型解析它应该进入哪些事件总线。
/// </summary>
public interface IEventRouteResolver
{
    IReadOnlyCollection<EventRoute> Resolve(Type eventType);
}
