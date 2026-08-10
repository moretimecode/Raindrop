using MediatR;
using System.Diagnostics;
using Raindrop.CQRS.Behaviors;
using Raindrop.CQRS.Commands;
using Raindrop.CQRS.Context;
using Raindrop.CQRS.Queries;

namespace Raindrop.CQRS.Tests;

/// <summary>
/// Pipeline Behavior 单元测试。
/// </summary>
public class BehaviorTests
{
    // ===================================================================
    //  AbstractPipelineBehavior — 基类行为
    // ===================================================================

    [Fact]
    public async Task AbstractPipelineBehavior_SetsRequestOnContext()
    {
        var accessor = new PipelineContextAccessor();
        var behavior = new TestGlobalBehavior(accessor);
        var request = new TestCmd();
        var expectedResponse = "result";

        var response = await behavior.Handle(request,
            ct => Task.FromResult(expectedResponse),
            CancellationToken.None);

        Assert.Same(request, accessor.Context.Request);
        Assert.Same(expectedResponse, accessor.Context.Response);
        Assert.Equal(expectedResponse, response);
    }

    [Fact]
    public async Task AbstractPipelineBehavior_InvokeAsyncIsCalled()
    {
        var accessor = new PipelineContextAccessor();
        var behavior = new TestGlobalBehavior(accessor);
        var request = new TestCmd();

        await behavior.Handle(request,
            ct => Task.FromResult("result"),
            CancellationToken.None);

        Assert.True(behavior.Invoked);
    }

    // ===================================================================
    //  CommandPipelineBehavior — 只对 ICommand<> 生效
    // ===================================================================

    [Fact]
    public async Task CommandPipelineBehavior_AcceptsCommand()
    {
        var accessor = new PipelineContextAccessor();
        var behavior = new TestCommandBehavior(accessor);
        var request = new TestCmd();

        var response = await behavior.Handle(request,
            ct => Task.FromResult("done"),
            CancellationToken.None);

        Assert.True(behavior.Invoked);
        Assert.Equal("done", response);
    }

    // ===================================================================
    //  QueryPipelineBehavior — 只对 IQuery<> 生效
    // ===================================================================

    [Fact]
    public async Task QueryPipelineBehavior_AcceptsQuery()
    {
        var accessor = new PipelineContextAccessor();
        var behavior = new TestQueryBehavior(accessor);
        var request = new TestQry();

        var response = await behavior.Handle(request,
            ct => Task.FromResult(100),
            CancellationToken.None);

        Assert.True(behavior.Invoked);
        Assert.Equal(100, response);
    }

    // ===================================================================
    //  GlobalPipelineBehavior — 对所有 TRequest : class 生效
    // ===================================================================

    [Fact]
    public async Task GlobalPipelineBehavior_AcceptsAnyClassRequest()
    {
        var accessor = new PipelineContextAccessor();
        var behavior = new TestGlobalBehavior(accessor);
        var request = new { Any = "thing" };

        var response = await behavior.Handle(request,
            ct => Task.FromResult("ok"),
            CancellationToken.None);

        Assert.True(behavior.Invoked);
        Assert.Equal("ok", response);
    }

    // ===================================================================
    //  DiagnosticBehavior — OpenTelemetry 追踪
    // ===================================================================

    [Fact]
    public async Task DiagnosticBehavior_CompletesWithoutError()
    {
        var accessor = new PipelineContextAccessor();
        var behavior = new DiagnosticBehavior<TestCmd, string>(accessor);
        var request = new TestCmd();

        var response = await behavior.Handle(request,
            ct => Task.FromResult("success"),
            CancellationToken.None);

        Assert.Equal("success", response);
    }

    [Fact]
    public async Task DiagnosticBehavior_SetsRequestAndResponseOnContext()
    {
        var accessor = new PipelineContextAccessor();
        var behavior = new DiagnosticBehavior<TestCmd, string>(accessor);
        var request = new TestCmd();

        await behavior.Handle(request,
            ct => Task.FromResult("ok"),
            CancellationToken.None);

        Assert.Same(request, accessor.Context.Request);
        Assert.Equal("ok", accessor.Context.Response);
    }

    [Fact]
    public async Task DiagnosticBehavior_PropagatesException()
    {
        var accessor = new PipelineContextAccessor();
        var behavior = new DiagnosticBehavior<TestCmd, string>(accessor);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => behavior.Handle(new TestCmd(),
                ct => throw new InvalidOperationException("fail"),
                CancellationToken.None));

        Assert.Equal("fail", ex.Message);
    }

    [Fact]
    public async Task DiagnosticBehavior_WhenNoListener_DoesNotClearCurrentActivity()
    {
        var accessor = new PipelineContextAccessor();
        var behavior = new DiagnosticBehavior<TestCmd, string>(accessor);
        using var outerActivity = new Activity("outer").Start();

        await behavior.Handle(new TestCmd(),
            ct => Task.FromResult("ok"),
            CancellationToken.None);

        Assert.Same(outerActivity, Activity.Current);
    }

    // ===================================================================
    //  测试类型
    // ===================================================================

    private sealed record TestCmd : ICommand<string>;
    private sealed record TestQry : IQuery<int>;

    private sealed class TestGlobalBehavior(IPipelineContextAccessor accessor)
        : GlobalPipelineBehavior<object, string>(accessor)
    {
        public bool Invoked { get; private set; }

        public override Task<string> InvokeAsync(object request,
            RequestHandlerDelegate<string> next,
            CancellationToken cancellationToken)
        {
            Invoked = true;
            return next(default);
        }
    }

    private sealed class TestCommandBehavior(IPipelineContextAccessor accessor)
        : CommandPipelineBehavior<TestCmd, string>(accessor)
    {
        public bool Invoked { get; private set; }

        public override Task<string> InvokeAsync(TestCmd request,
            RequestHandlerDelegate<string> next,
            CancellationToken cancellationToken)
        {
            Invoked = true;
            return next(default);
        }
    }

    private sealed class TestQueryBehavior(IPipelineContextAccessor accessor)
        : QueryPipelineBehavior<TestQry, int>(accessor)
    {
        public bool Invoked { get; private set; }

        public override Task<int> InvokeAsync(TestQry request,
            RequestHandlerDelegate<int> next,
            CancellationToken cancellationToken)
        {
            Invoked = true;
            return next(default);
        }
    }
}
