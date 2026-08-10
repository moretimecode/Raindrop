using Raindrop.CQRS.Context;

namespace Raindrop.CQRS.Tests;

/// <summary>
/// PipelineContext 单元测试。
/// </summary>
public class PipelineContextTests
{
    [Fact]
    public void CreateNew_ReturnsFreshContext()
    {
        var ctx = PipelineContext.CreateNew();

        Assert.Null(ctx.Request);
        Assert.Null(ctx.Response);
        Assert.Empty(ctx.Data);
    }

    [Fact]
    public void SetRequest_StoresRequest()
    {
        var ctx = PipelineContext.CreateNew();
        var request = new { Name = "test" };

        ctx.SetRequest(request);

        Assert.Same(request, ctx.Request);
    }

    [Fact]
    public void SetResponse_StoresResponse()
    {
        var ctx = PipelineContext.CreateNew();
        var response = new { Result = 42 };

        ctx.SetResponse(response);

        Assert.Same(response, ctx.Response);
    }

    [Fact]
    public void Clear_ResetsAllState()
    {
        var ctx = PipelineContext.CreateNew();
        ctx.SetRequest(new { A = 1 });
        ctx.SetResponse(new { B = 2 });
        ctx.SetData("key", "value");

        ctx.Clear();

        Assert.Null(ctx.Request);
        Assert.Null(ctx.Response);
        Assert.Empty(ctx.Data);
    }

    [Fact]
    public void SetData_WithoutOverwrite_IgnoresDuplicateKey()
    {
        var ctx = PipelineContext.CreateNew();

        ctx.SetData("key", "first");
        ctx.SetData("key", "second", isOverWrite: false);

        Assert.Equal("first", ctx.GetData("key"));
    }

    [Fact]
    public void SetData_WithOverwrite_ReplacesValue()
    {
        var ctx = PipelineContext.CreateNew();

        ctx.SetData("key", "first");
        ctx.SetData("key", "second", isOverWrite: true);

        Assert.Equal("second", ctx.GetData("key"));
    }

    [Fact]
    public void GetData_MissingKey_ReturnsNull()
    {
        var ctx = PipelineContext.CreateNew();

        var result = ctx.GetData("nonexistent");

        Assert.Null(result);
    }

    [Fact]
    public void GetData_ReturnsStoredValue()
    {
        var ctx = PipelineContext.CreateNew();
        ctx.SetData("key", 123);

        var result = ctx.GetData("key");

        Assert.Equal(123, result);
    }

    [Fact]
    public void Data_ReturnsReadOnlyView()
    {
        var ctx = PipelineContext.CreateNew();
        ctx.SetData("a", 1);
        ctx.SetData("b", 2);

        var data = ctx.Data;

        Assert.Equal(2, data.Count);
        Assert.Equal(1, data["a"]);
        Assert.Equal(2, data["b"]);
    }
}
