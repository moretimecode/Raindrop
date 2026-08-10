namespace Raindrop.EventBus.Abstractions;

/// <summary>
/// 显式声明事件需要进入本地进程内事件总线。
/// </summary>
public sealed class LocalEventAttribute : EventRouteAttribute
{
}
