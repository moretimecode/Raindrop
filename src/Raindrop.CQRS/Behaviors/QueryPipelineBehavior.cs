using Raindrop.CQRS.Context;
using Raindrop.CQRS.Queries;

namespace Raindrop.CQRS.Behaviors;

/// <summary>
/// 查询级 Pipeline Behavior。仅对 <see cref="IQuery{TResponse}"/> 生效。
/// </summary>
/// <typeparam name="TRequest">查询类型</typeparam>
/// <typeparam name="TResponse">返回类型</typeparam>
public abstract class QueryPipelineBehavior<TRequest, TResponse> : AbstractPipelineBehavior<TRequest, TResponse>
    where TRequest : class, IQuery<TResponse>
{
    /// <summary>
    /// 初始化查询级行为。
    /// </summary>
    protected QueryPipelineBehavior(IPipelineContextAccessor accessor) : base(accessor)
    {
    }
}
