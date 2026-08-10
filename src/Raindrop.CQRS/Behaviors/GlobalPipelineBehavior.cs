using Raindrop.CQRS.Context;

namespace Raindrop.CQRS.Behaviors;

/// <summary>
/// 全局 Pipeline Behavior。对所有 class 类型的请求生效。
/// </summary>
/// <typeparam name="TRequest">请求类型</typeparam>
/// <typeparam name="TResponse">响应类型</typeparam>
public abstract class GlobalPipelineBehavior<TRequest, TResponse> : AbstractPipelineBehavior<TRequest, TResponse>
    where TRequest : class
{
    /// <summary>
    /// 初始化全局行为。
    /// </summary>
    protected GlobalPipelineBehavior(IPipelineContextAccessor accessor) : base(accessor)
    {
    }
}
