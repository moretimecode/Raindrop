using Raindrop.CQRS.Commands;
using Raindrop.CQRS.Context;

namespace Raindrop.CQRS.Behaviors;

/// <summary>
/// 命令级 Pipeline Behavior。仅对 <see cref="ICommand{TResponse}"/> 生效。
/// </summary>
/// <typeparam name="TRequest">命令类型</typeparam>
/// <typeparam name="TResponse">返回类型</typeparam>
public abstract class CommandPipelineBehavior<TRequest, TResponse> : AbstractPipelineBehavior<TRequest, TResponse>
    where TRequest : class, ICommand<TResponse>
{
    /// <summary>
    /// 初始化命令级行为。
    /// </summary>
    protected CommandPipelineBehavior(IPipelineContextAccessor accessor) : base(accessor)
    {
    }
}
