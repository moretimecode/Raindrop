using MediatR;
using Raindrop.CQRS.Context;
using Raindrop.CQRS.Queries;

namespace Raindrop.CQRS.Bus;

/// <summary>
/// 查询总线默认实现。每次调用前自动清空 <see cref="PipelineContext"/>。
/// </summary>
public class QueryBus : IQueryBus
{
    private readonly IMediator _mediator;
    private readonly IPipelineContextAccessor _accessor;

    /// <summary>
    /// 初始化查询总线。
    /// </summary>
    public QueryBus(IMediator mediator, IPipelineContextAccessor accessor)
    {
        _mediator = mediator;
        _accessor = accessor;
    }

    /// <inheritdoc />
    public async Task<TResponse> Send<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default)
    {
        _accessor.ClearContext();
        return await _mediator.Send(query, cancellationToken);
    }
}
