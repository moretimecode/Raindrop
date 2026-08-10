using Raindrop.CQRS.Context;

namespace Raindrop.CQRS.Commands;

/// <summary>
/// Handler 上下文基类。从 <see cref="IPipelineContextAccessor"/> 获取当前管道上下文。
/// </summary>
public abstract class HandlerContext
{
    /// <summary>
    /// 当前管道上下文。
    /// </summary>
    protected PipelineContext Context { get; set; }

    /// <summary>
    /// 初始化 HandlerContext。
    /// </summary>
    protected HandlerContext(IPipelineContextAccessor accessor)
    {
        Context = accessor.Context;
    }
}
