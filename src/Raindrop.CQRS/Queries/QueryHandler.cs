using Raindrop.CQRS.Context;

namespace Raindrop.CQRS.Queries;

/// <summary>
/// 查询处理器基类。
/// </summary>
/// <typeparam name="TQuery">查询类型</typeparam>
/// <typeparam name="TResponse">返回类型</typeparam>
public abstract class QueryHandler<TQuery, TResponse> : IQueryHandler<TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    /// <summary>
    /// 当前管道上下文。
    /// </summary>
    protected PipelineContext Context { get; set; }

    /// <summary>
    /// 初始化查询处理器并获取当前管道上下文。
    /// </summary>
    protected QueryHandler(IPipelineContextAccessor accessor)
    {
        Context = accessor.Context;
    }

    /// <summary>
    /// 处理查询。
    /// </summary>
    public abstract Task<TResponse> Handle(TQuery request, CancellationToken cancellationToken);
}
