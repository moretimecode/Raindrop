using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Raindrop.CQRS;
using Raindrop.CQRS.Bus;
using Raindrop.CQRS.Commands;
using Raindrop.CQRS.Context;
using Raindrop.CQRS.FluentValidation;
using Raindrop.CQRS.Queries;
using Raindrop.Utility;

namespace Raindrop.CQRS.Sample.Scenarios;

/// <summary>
/// 演示 Raindrop.CQRS 集成：
///   - 有返回值命令（ICommand&lt;TResponse&gt;）
///   - 无返回值命令（ICommand + NullableCommandHandler）
///   - 查询（IQuery&lt;TResponse&gt;）
///   - FluentValidation 验证管道
///   - 自定义 Pipeline Behavior
///   - PipelineContext 数据传递
///   - Bus 自动清理上下文
/// </summary>
public static class CQRSSampleScenario
{
    public static async Task RunAsync()
    {
        Console.WriteLine("===== Raindrop.CQRS 示例 =====\n");

        var services = new ServiceCollection();
        services.AddLogging();

        // 注册 CQRS + FluentValidation
        services.AddRaindropCQRS(
                applicationAssemblies: new[] { typeof(CQRSSampleScenario).Assembly })
            .AddValidation(options =>
            {
                options.DefaultErrorCode = 400;
                options.ErrorMessageMaxLength = 200;
            });

        await using var provider = services.BuildServiceProvider();
        var commandBus = provider.GetRequiredService<ICommandBus>();
        var queryBus = provider.GetRequiredService<IQueryBus>();

        // ═══════════════════════════════════════════════════
        //  1. 有返回值命令
        // ═══════════════════════════════════════════════════
        Console.WriteLine("--- 1. 有返回值命令 ---");
        var createResult = await commandBus.Send(new CreateUserCommand("Alice", "alice@example.com"));
        Console.WriteLine($"  创建用户成功：Id={createResult.Id}, Name={createResult.Name}");
        Console.WriteLine();

        // ═══════════════════════════════════════════════════
        //  2. 查询
        // ═══════════════════════════════════════════════════
        Console.WriteLine("--- 2. 查询 ---");
        var user = await queryBus.Send(new GetUserQuery(createResult.Id));
        Console.WriteLine($"  查询结果：Id={user!.Id}, Name={user.Name}, Email={user.Email}");
        Console.WriteLine();

        // ═══════════════════════════════════════════════════
        //  3. 无返回值命令（NullableCommandHandler）
        // ═══════════════════════════════════════════════════
        Console.WriteLine("--- 3. 无返回值命令 ---");
        await commandBus.Send(new ActivateUserCommand(createResult.Id));
        Console.WriteLine("  用户已激活（无返回值）");
        Console.WriteLine();

        // ═══════════════════════════════════════════════════
        //  4. FluentValidation 验证失败
        // ═══════════════════════════════════════════════════
        Console.WriteLine("--- 4. 验证失败 ---");
        try
        {
            await commandBus.Send(new CreateUserCommand("", "invalid-email"));
        }
        catch (FriendlyException ex)
        {
            Console.WriteLine($"  验证失败被拦截：Code={ex.Code}");
            Console.WriteLine($"  错误消息：{ex.Message}");
        }
        Console.WriteLine();

        // ═══════════════════════════════════════════════════
        //  5. 业务异常（在 Handler 中抛出）
        // ═══════════════════════════════════════════════════
        Console.WriteLine("--- 5. 业务异常 ---");
        try
        {
            // 删除一个不存在的用户
            await commandBus.Send(new DeleteUserCommand(Guid.NewGuid()));
        }
        catch (FriendlyException ex)
        {
            Console.WriteLine($"  业务异常：Code={ex.Code}, Message={ex.Message}");
        }
        Console.WriteLine();

        // ═══════════════════════════════════════════════════
        //  6. PipelineContext 数据传递
        // ═══════════════════════════════════════════════════
        Console.WriteLine("--- 6. PipelineContext ---");
        await commandBus.Send(new ContextDemoCommand("测试上下文"));
        Console.WriteLine();

        Console.WriteLine("===== 示例完成 =====");
    }
}

// ═══════════════════════════════════════════════════════════
//  命令与结果定义
// ═══════════════════════════════════════════════════════════

/// <summary>
/// 创建用户命令。
/// </summary>
public sealed record CreateUserCommand(string Name, string Email) : ICommand<CreateUserResult>;

/// <summary>
/// 创建用户结果。
/// </summary>
public sealed record CreateUserResult(Guid Id, string Name);

/// <summary>
/// 激活用户命令（无返回值）。
/// </summary>
public sealed record ActivateUserCommand(Guid UserId) : ICommand;

/// <summary>
/// 删除用户命令（无返回值）。
/// </summary>
public sealed record DeleteUserCommand(Guid UserId) : ICommand;

/// <summary>
/// 上下文演示命令。
/// </summary>
public sealed record ContextDemoCommand(string Message) : ICommand;

/// <summary>
/// 查询用户。
/// </summary>
public sealed record GetUserQuery(Guid UserId) : IQuery<UserDto?>;

/// <summary>
/// 用户 DTO。
/// </summary>
public sealed record UserDto(Guid Id, string Name, string Email, bool Active);

// ═══════════════════════════════════════════════════════════
//  Handler 实现
// ═══════════════════════════════════════════════════════════

/// <summary>
/// 创建用户处理器（有返回值）。
/// </summary>
public sealed class CreateUserCommandHandler : CommandHandler<CreateUserCommand, CreateUserResult>
{
    public CreateUserCommandHandler(IPipelineContextAccessor accessor) : base(accessor)
    {
    }

    public override Task<CreateUserResult> Handle(CreateUserCommand request, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        // 模拟存储
        UserStore.Users[id] = new UserDto(id, request.Name, request.Email, false);

        return Task.FromResult(new CreateUserResult(id, request.Name));
    }
}

/// <summary>
/// 激活用户处理器（NullableCommandHandler）。
/// </summary>
public sealed class ActivateUserCommandHandler : NullableCommandHandler<ActivateUserCommand>
{
    public ActivateUserCommandHandler(IPipelineContextAccessor accessor) : base(accessor)
    {
    }

    public override Task Execute(ActivateUserCommand request, CancellationToken cancellationToken)
    {
        if (!UserStore.Users.TryGetValue(request.UserId, out var user))
            throw new FriendlyException($"用户 {request.UserId} 不存在", 10404);

        UserStore.Users[request.UserId] = user with { Active = true };
        return Task.CompletedTask;
    }
}

/// <summary>
/// 删除用户处理器（无返回值 Command）。
/// </summary>
public sealed class DeleteUserCommandHandler : CommandHandler<DeleteUserCommand>
{
    public DeleteUserCommandHandler(IPipelineContextAccessor accessor) : base(accessor)
    {
    }

    public override Task<NullableResponse> Handle(DeleteUserCommand request, CancellationToken cancellationToken)
    {
        if (!UserStore.Users.ContainsKey(request.UserId))
            throw new FriendlyException($"用户 {request.UserId} 不存在，无法删除", 10404);

        UserStore.Users.Remove(request.UserId);
        return Task.FromResult(NullableResponse.Value);
    }
}

/// <summary>
/// 查询用户处理器。
/// </summary>
public sealed class GetUserQueryHandler : QueryHandler<GetUserQuery, UserDto?>
{
    public GetUserQueryHandler(IPipelineContextAccessor accessor) : base(accessor)
    {
    }

    public override Task<UserDto?> Handle(GetUserQuery request, CancellationToken cancellationToken)
    {
        UserStore.Users.TryGetValue(request.UserId, out var user);
        return Task.FromResult(user);
    }
}

/// <summary>
/// 上下文演示处理器。
/// </summary>
public sealed class ContextDemoCommandHandler : NullableCommandHandler<ContextDemoCommand>
{
    public ContextDemoCommandHandler(IPipelineContextAccessor accessor) : base(accessor)
    {
    }

    public override Task Execute(ContextDemoCommand request, CancellationToken cancellationToken)
    {
        // 写入自定义数据到 PipelineContext
        Context.SetData("demo-key", "demo-value");
        Context.SetData("timestamp", DateTime.UtcNow);

        Console.WriteLine($"  Handler 收到消息：{request.Message}");
        Console.WriteLine($"  Context.Request = {Context.Request?.GetType().Name}");
        Console.WriteLine($"  Context.Data[\"demo-key\"] = {Context.GetData("demo-key")}");

        return Task.CompletedTask;
    }
}

// ═══════════════════════════════════════════════════════════
//  FluentValidation 验证器
// ═══════════════════════════════════════════════════════════

/// <summary>
/// 创建用户命令验证器。
/// </summary>
public sealed class CreateUserCommandValidator : AbstractValidator<CreateUserCommand>
{
    public CreateUserCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("用户名不能为空").WithErrorCode(10001);

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("邮箱不能为空").WithErrorCode(10002)
            .EmailAddress().WithMessage("邮箱格式不正确").WithErrorCode(10003);
    }
}

// ═══════════════════════════════════════════════════════════
//  模拟存储
// ═══════════════════════════════════════════════════════════

/// <summary>
/// 简单的内存存储，仅用于演示。
/// </summary>
public static class UserStore
{
    public static readonly Dictionary<Guid, UserDto> Users = [];
}
