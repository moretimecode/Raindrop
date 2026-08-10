using MediatR;
using Raindrop.CQRS.Commands;
using Raindrop.CQRS.Context;

namespace Raindrop.CQRS.Bus;

/// <summary>
/// 命令总线默认实现。每次调用前自动清空 <see cref="PipelineContext"/>。
/// </summary>
public class CommandBus : ICommandBus
{
    private readonly IMediator _mediator;
    private readonly IPipelineContextAccessor _accessor;

    /// <summary>
    /// 初始化命令总线。
    /// </summary>
    public CommandBus(IMediator mediator, IPipelineContextAccessor accessor)
    {
        _mediator = mediator;
        _accessor = accessor;
    }

    /// <inheritdoc />
    public async Task Send<TCommand>(TCommand command, CancellationToken cancellationToken = default)
        where TCommand : ICommand
    {
        _accessor.ClearContext();
        await _mediator.Send(command, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<TResponse> Send<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default)
    {
        _accessor.ClearContext();
        return await _mediator.Send(command, cancellationToken);
    }
}
