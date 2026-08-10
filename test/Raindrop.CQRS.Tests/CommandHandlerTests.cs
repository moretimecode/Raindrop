using Raindrop.CQRS.Commands;
using Raindrop.CQRS.Context;

namespace Raindrop.CQRS.Tests;

/// <summary>
/// CommandHandler 单元测试。
/// </summary>
public class CommandHandlerTests
{
    // ===================================================================
    //  有返回值 CommandHandler
    // ===================================================================

    [Fact]
    public async Task CommandHandler_InvokesHandleWithCorrectRequest()
    {
        var accessor = new PipelineContextAccessor();
        var handler = new CreateUserHandler(accessor);

        var request = new CreateUser("alice");
        var result = await handler.Handle(request, CancellationToken.None);

        Assert.Equal("alice", result.Name);
        Assert.Equal(1, result.Id);
    }

    [Fact]
    public void CommandHandler_HasAccessToContext_ViaHandlerContext()
    {
        var accessor = new PipelineContextAccessor();
        var handler = new CreateUserHandler(accessor);

        var context = GetHandlerContext(handler);

        Assert.NotNull(context);
        Assert.Same(accessor.Context, context);
    }

    // ===================================================================
    //  无返回值 CommandHandler（ICommand）
    // ===================================================================

    [Fact]
    public async Task VoidCommandHandler_ReturnsNullableResponseValue()
    {
        var accessor = new PipelineContextAccessor();
        var handler = new DeleteUserHandler(accessor);

        var result = await handler.Handle(new DeleteUser(99), CancellationToken.None);

        Assert.Equal(NullableResponse.Value, result);
    }

    // ===================================================================
    //  NullableCommandHandler
    // ===================================================================

    [Fact]
    public async Task NullableCommandHandler_ExecutesAndReturnsNullableResponse()
    {
        var accessor = new PipelineContextAccessor();
        var handler = new TestNullableHandler(accessor);

        var request = new SendNotification("hello");
        var result = await handler.Handle(request, CancellationToken.None);

        Assert.True(handler.Executed);
        Assert.Equal("hello", handler.ReceivedMessage);
        Assert.Equal(NullableResponse.Value, result);
    }

    // ===================================================================
    //  Helpers
    // ===================================================================

    private static PipelineContext GetHandlerContext(object handler)
    {
        var prop = handler.GetType().BaseType!
            .GetProperty("Context",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (PipelineContext)prop!.GetValue(handler)!;
    }

    // ===================================================================
    //  测试用命令、结果、Handler
    // ===================================================================

    private sealed record CreateUser(string Name) : ICommand<CreateUserResult>;
    private sealed record CreateUserResult(int Id, string Name);

    private sealed class CreateUserHandler(IPipelineContextAccessor accessor)
        : CommandHandler<CreateUser, CreateUserResult>(accessor)
    {
        public override Task<CreateUserResult> Handle(CreateUser request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new CreateUserResult(1, request.Name));
        }
    }

    private sealed record DeleteUser(int Id) : ICommand;

    private sealed class DeleteUserHandler(IPipelineContextAccessor accessor)
        : CommandHandler<DeleteUser>(accessor)
    {
        public override Task<NullableResponse> Handle(DeleteUser request, CancellationToken cancellationToken)
        {
            return Task.FromResult(NullableResponse.Value);
        }
    }

    private sealed record SendNotification(string Message) : ICommand;

    private sealed class TestNullableHandler(IPipelineContextAccessor accessor)
        : NullableCommandHandler<SendNotification>(accessor)
    {
        public bool Executed { get; private set; }
        public string? ReceivedMessage { get; private set; }

        public override Task Execute(SendNotification request, CancellationToken cancellationToken)
        {
            Executed = true;
            ReceivedMessage = request.Message;
            return Task.CompletedTask;
        }
    }
}
