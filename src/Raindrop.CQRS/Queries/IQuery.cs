using MediatR;

namespace Raindrop.CQRS.Queries;

/// <summary>
/// 查询接口。继承 MediatR 的 <see cref="IRequest{TResponse}"/>。
/// </summary>
/// <typeparam name="TResponse">查询返回类型</typeparam>
public interface IQuery<out TResponse> : IRequest<TResponse>
{
}
