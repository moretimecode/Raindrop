using Raindrop.CQRS.Context;
using Raindrop.CQRS.Queries;

namespace Raindrop.CQRS.Tests;

/// <summary>
/// QueryHandler 单元测试。
/// </summary>
public class QueryHandlerTests
{
    private sealed record GetNameQuery(int Id) : IQuery<string>;
    private sealed record GetCountQuery : IQuery<int>;

    [Fact]
    public async Task QueryHandler_InvokesHandleWithRequest()
    {
        var accessor = new PipelineContextAccessor();
        var handler = new TestQueryHandler(accessor);

        var request = new GetNameQuery(42);
        var result = await handler.Handle(request, CancellationToken.None);

        Assert.Equal("Name:42", result);
    }

    [Fact]
    public void QueryHandler_HasAccessToContext()
    {
        var accessor = new PipelineContextAccessor();
        var handler = new TestQueryHandler(accessor);

        var context = GetContext(handler);

        Assert.NotNull(context);
        Assert.Same(accessor.Context, context);
    }

    [Fact]
    public async Task QueryHandler_NoParamCommand_Works()
    {
        var accessor = new PipelineContextAccessor();
        var handler = new CountQueryHandler(accessor);

        var result = await handler.Handle(new GetCountQuery(), CancellationToken.None);

        Assert.Equal(100, result);
    }

    private static PipelineContext GetContext(object handler)
    {
        var prop = handler.GetType().BaseType!
            .GetProperty("Context", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (PipelineContext)prop!.GetValue(handler)!;
    }

    private sealed class TestQueryHandler(IPipelineContextAccessor accessor)
        : QueryHandler<GetNameQuery, string>(accessor)
    {
        public override Task<string> Handle(GetNameQuery request, CancellationToken cancellationToken)
        {
            return Task.FromResult($"Name:{request.Id}");
        }
    }

    private sealed class CountQueryHandler(IPipelineContextAccessor accessor)
        : QueryHandler<GetCountQuery, int>(accessor)
    {
        public override Task<int> Handle(GetCountQuery request, CancellationToken cancellationToken)
        {
            return Task.FromResult(100);
        }
    }
}
