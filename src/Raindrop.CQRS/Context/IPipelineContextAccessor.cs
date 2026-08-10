namespace Raindrop.CQRS.Context;

/// <summary>
/// 管道上下文访问器。
/// </summary>
public interface IPipelineContextAccessor
{
    /// <summary>
    /// 当前管道上下文。
    /// </summary>
    PipelineContext Context { get; set; }

    /// <summary>
    /// 清空当前上下文。
    /// </summary>
    void ClearContext();
}
