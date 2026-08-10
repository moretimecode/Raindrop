using Raindrop.CQRS.Commands;

namespace Raindrop.CQRS.Bus;

/// <summary>
/// 命令总线接口。封装 MediatR，提供清晰的有/无返回值命令发送语义。
/// </summary>
public interface ICommandBus
{
    /// <summary>
    /// 发送无返回值命令。
    /// </summary>
    Task Send<TCommand>(TCommand command, CancellationToken cancellationToken = default)
        where TCommand : ICommand;

    /// <summary>
    /// 发送有返回值命令。
    /// </summary>
    Task<TResponse> Send<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default);
}
