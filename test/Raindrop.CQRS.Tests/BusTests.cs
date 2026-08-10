using MediatR;
using Raindrop.CQRS.Bus;
using Raindrop.CQRS.Commands;
using Raindrop.CQRS.Context;
using Raindrop.CQRS.Queries;

namespace Raindrop.CQRS.Tests;

/// <summary>
/// ICommandBus / IQueryBus 单元测试。
/// </summary>
public class BusTests
{
    // ===================================================================
    //  CommandBus — 无返回值命令
    // ===================================================================

    [Fact]
    public async Task CommandBus_SendVoidCommand_DelegatesToMediator()
    {
        var mediator = new FakeMediator();
        var accessor = new PipelineContextAccessor();
        var bus = new CommandBus(mediator, accessor);
        var cmd = new PingCommand();

        await bus.Send(cmd);

        Assert.Same(cmd, mediator.LastRequest);
    }

    [Fact]
    public async Task CommandBus_SendVoidCommand_ClearsContextBeforeSending()
    {
        var mediator = new FakeMediator();
        var accessor = new PipelineContextAccessor();
        accessor.Context.SetData("stale-key", "stale-value");
        var bus = new CommandBus(mediator, accessor);

        await bus.Send(new PingCommand());

        Assert.Empty(accessor.Context.Data);
    }

    // ===================================================================
    //  CommandBus — 有返回值命令
    // ===================================================================

    [Fact]
    public async Task CommandBus_SendCommandWithResponse_ReturnsResponse()
    {
        var mediator = new FakeMediator { Result = new PongResult("ok") };
        var accessor = new PipelineContextAccessor();
        var bus = new CommandBus(mediator, accessor);

        var result = await bus.Send(new PongCommand());

        Assert.Equal("ok", result.Message);
    }

    [Fact]
    public async Task CommandBus_SendCommandWithResponse_ClearsContextBeforeSending()
    {
        var mediator = new FakeMediator { Result = new PongResult("ok") };
        var accessor = new PipelineContextAccessor();
        accessor.Context.SetData("key", "value");
        var bus = new CommandBus(mediator, accessor);

        await bus.Send(new PongCommand());

        Assert.Empty(accessor.Context.Data);
    }

    // ===================================================================
    //  QueryBus
    // ===================================================================

    [Fact]
    public async Task QueryBus_Send_ReturnsResponse()
    {
        var mediator = new FakeMediator { Result = 42 };
        var accessor = new PipelineContextAccessor();
        var bus = new QueryBus(mediator, accessor);

        var result = await bus.Send(new GetNumberQuery());

        Assert.Equal(42, result);
    }

    [Fact]
    public async Task QueryBus_Send_ClearsContextBeforeSending()
    {
        var mediator = new FakeMediator { Result = 99 };
        var accessor = new PipelineContextAccessor();
        accessor.Context.SetData("old", "data");
        var bus = new QueryBus(mediator, accessor);

        await bus.Send(new GetNumberQuery());

        Assert.Empty(accessor.Context.Data);
    }

    // ===================================================================
    //  测试命令/查询
    // ===================================================================

    private sealed record PingCommand : ICommand;
    private sealed record PongCommand : ICommand<PongResult>;
    private sealed record PongResult(string Message);
    private sealed record GetNumberQuery : IQuery<int>;

    // ===================================================================
    //  FakeMediator
    // ===================================================================

    private sealed class FakeMediator : IMediator
    {
        public object? LastRequest { get; private set; }
        public object? Result { get; set; }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            if (Result is TResponse typed)
                return Task.FromResult(typed);
            return Task.FromResult(default(TResponse)!);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            LastRequest = request;
            return Task.CompletedTask;
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(Result);
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Publish<TNotification>(TNotification notification,
            CancellationToken cancellationToken = default) where TNotification : INotification =>
            throw new NotSupportedException();

        public Task Publish(object notification, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
