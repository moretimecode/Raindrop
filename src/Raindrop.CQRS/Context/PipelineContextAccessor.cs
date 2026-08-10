namespace Raindrop.CQRS.Context;

/// <summary>
/// 管道上下文访问器默认实现（Scoped）。
/// </summary>
public class PipelineContextAccessor : IPipelineContextAccessor
{
    private PipelineContext _context;
    private readonly object _contextLock = new();

    public PipelineContextAccessor()
    {
        _context = PipelineContext.CreateNew();
    }

    public PipelineContext Context
    {
        get => _context;
        set => _context = value;
    }

    /// <summary>
    /// 清空当前上下文。如果上下文为 null 则先创建新实例。
    /// </summary>
    public void ClearContext()
    {
        if (Context is null)
        {
            lock (_contextLock)
            {
                if (Context is null)
                    Context = PipelineContext.CreateNew();
            }
        }

        Context.Clear();
    }
}
