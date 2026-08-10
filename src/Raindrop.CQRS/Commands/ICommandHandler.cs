using MediatR;

namespace Raindrop.CQRS.Commands;

/// <summary>
/// 无返回值命令处理器（内部接口）。
/// </summary>
internal interface ICommandHandler<in TCommand> : IRequestHandler<TCommand, NullableResponse>
    where TCommand : ICommand
{
}

/// <summary>
/// 有返回值命令处理器（内部接口）。
/// </summary>
internal interface ICommandHandler<in TCommand, TResponse> : IRequestHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
}
