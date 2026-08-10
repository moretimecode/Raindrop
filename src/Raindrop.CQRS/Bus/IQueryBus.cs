using Raindrop.CQRS.Queries;

namespace Raindrop.CQRS.Bus;

/// <summary>
/// 查询总线接口。封装 MediatR，语义上只做查询。
/// </summary>
public interface IQueryBus
{
    /// <summary>
    /// 发送查询。
    /// </summary>
    Task<TResponse> Send<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default);
}
