using Raindrop.CQRS.Context;

namespace Raindrop.CQRS.Tests;

/// <summary>
/// PipelineContextAccessor 单元测试。
/// </summary>
public class PipelineContextAccessorTests
{
    [Fact]
    public void Context_AfterConstruction_IsNotNull()
    {
        var accessor = new PipelineContextAccessor();

        Assert.NotNull(accessor.Context);
    }

    [Fact]
    public void ClearContext_ClearsTheContext()
    {
        var accessor = new PipelineContextAccessor();
        accessor.Context.SetRequest(new { Name = "test" });
        accessor.Context.SetData("key", "value");

        accessor.ClearContext();

        Assert.Null(accessor.Context.Request);
        Assert.Empty(accessor.Context.Data);
    }

    [Fact]
    public void ClearContext_WhenContextIsNull_CreatesNewAndClears()
    {
        var accessor = new PipelineContextAccessor();
        accessor.Context = null!;

        // 不应抛出异常
        accessor.ClearContext();

        Assert.NotNull(accessor.Context);
        Assert.Null(accessor.Context.Request);
    }

    [Fact]
    public void Context_SetAndGet_Works()
    {
        var accessor = new PipelineContextAccessor();
        var newContext = PipelineContext.CreateNew();

        accessor.Context = newContext;

        Assert.Same(newContext, accessor.Context);
    }
}
