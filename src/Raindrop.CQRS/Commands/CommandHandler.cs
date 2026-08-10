using Raindrop.CQRS.Context;

namespace Raindrop.CQRS.Commands;

/// <summary>
/// 有返回值命令处理器基类。
/// </summary>
/// <typeparam name="TCommand">命令类型</typeparam>
/// <typeparam name="TResponse">返回类型</typeparam>
public abstract class CommandHandler<TCommand, TResponse> : HandlerContext, ICommandHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    /// <summary>
    /// 初始化命令处理器并获取当前管道上下文。
    /// </summary>
    protected CommandHandler(IPipelineContextAccessor accessor) : base(accessor)
    {
    }

    /// <summary>
    /// 处理命令。
    /// </summary>
    public abstract Task<TResponse> Handle(TCommand request, CancellationToken cancellationToken);
}

/// <summary>
/// 无返回值命令处理器基类。
/// </summary>
/// <typeparam name="TCommand">命令类型</typeparam>
public abstract class CommandHandler<TCommand> : HandlerContext, ICommandHandler<TCommand, NullableResponse>
    where TCommand : ICommand<NullableResponse>
{
    /// <summary>
    /// 初始化命令处理器并获取当前管道上下文。
    /// </summary>
    protected CommandHandler(IPipelineContextAccessor accessor) : base(accessor)
    {
    }

    /// <summary>
    /// 处理命令。
    /// </summary>
    public abstract Task<NullableResponse> Handle(TCommand request, CancellationToken cancellationToken);
}

/// <summary>
/// 无返回值命令便捷基类。只需重写 <see cref="Execute"/> 方法。
/// </summary>
/// <typeparam name="TCommand">命令类型</typeparam>
public abstract class NullableCommandHandler<TCommand> : HandlerContext, ICommandHandler<TCommand, NullableResponse>
    where TCommand : ICommand<NullableResponse>
{
    /// <summary>
    /// 初始化命令处理器并获取当前管道上下文。
    /// </summary>
    protected NullableCommandHandler(IPipelineContextAccessor accessor) : base(accessor)
    {
    }

    /// <summary>
    /// 处理命令。内部调用 <see cref="Execute"/>，自动返回 <see cref="NullableResponse.Value"/>。
    /// </summary>
    public async Task<NullableResponse> Handle(TCommand request, CancellationToken cancellationToken)
    {
        await Execute(request, cancellationToken);

        return NullableResponse.Value;
    }

    /// <summary>
    /// 执行命令。无需返回任何值。
    /// </summary>
    public abstract Task Execute(TCommand request, CancellationToken cancellationToken);
}
