using MediatR;

namespace Raindrop.CQRS.Commands;

/// <summary>
/// 无返回值命令。继承 <see cref="ICommand{NullableResponse}"/>。
/// </summary>
public interface ICommand : ICommand<NullableResponse>
{
}

/// <summary>
/// 有返回值命令。继承 MediatR 的 <see cref="IRequest{TResponse}"/>。
/// </summary>
/// <typeparam name="TResponse">命令返回类型</typeparam>
public interface ICommand<out TResponse> : IRequest<TResponse>
{
}
