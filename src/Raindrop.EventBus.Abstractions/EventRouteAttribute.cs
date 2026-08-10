namespace Raindrop.EventBus.Abstractions;

/// <summary>
/// 事件路由特性的基类。具体事件总线可以继承它来定义自己的元数据。
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true, Inherited = false)]
public abstract class EventRouteAttribute : Attribute
{
}
