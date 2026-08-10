using MediatR;

namespace Raindrop.CQRS.Queries;

/// <summary>
/// 查询处理器（内部接口）。
/// </summary>
internal interface IQueryHandler<in TQuery, TResponse> : IRequestHandler<TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
}
