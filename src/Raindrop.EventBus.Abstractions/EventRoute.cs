namespace Raindrop.EventBus.Abstractions;

/// <summary>
/// 事件发布时解析出的路由信息。
/// </summary>
public sealed class EventRoute
{
    public EventRoute(EventRouteAttribute metadata)
    {
        Metadata = metadata;
    }

    public EventRouteAttribute Metadata { get; }
}
