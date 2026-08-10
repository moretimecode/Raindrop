---
name: add-cqrs-command
description: 在 Raindrop 项目中添加一个新的 CQRS 命令（Command）、查询（Query）及其 Handler。当用户需要新增业务操作（创建/更新/删除/查询）时使用。触发词：添加命令、新增 Command、新增 Query、添加 Handler、CQRS 操作、新增业务接口、添加 API。
---

# 添加 CQRS 命令/查询

在 Raindrop 项目中，按 CQRS 模式新增一个命令或查询，需要同时创建请求类型、Handler 和可选的 FluentValidation 验证器。

## 前置知识

参考文档：
- [docs/cqrs.md](docs/cqrs.md) — CQRS 完整设计说明
- [project-overview](.claude/skills/raindrop-project-overview/SKILL.md) — 项目架构总览

## 步骤

### 1. 确定命令/查询类型

根据业务操作选择：

| 场景 | 接口 | Handler 基类 | 返回值 |
|------|------|-------------|--------|
| 写操作，无返回值 | `ICommand` | `NullableCommandHandler<TCommand>` | `NullableResponse` |
| 写操作，有返回值 | `ICommand<TResponse>` | `CommandHandler<TCommand, TResponse>` | `TResponse` |
| 读操作 | `IQuery<TResponse>` | `QueryHandler<TQuery, TResponse>` | `TResponse` |

### 2. 创建请求 Record

命令（有返回值）：
```csharp
// Commands/CreateOrderCommand.cs
public sealed record CreateOrderCommand(
    string CustomerName,
    decimal Amount) : ICommand<CreateOrderResult>;

public sealed record CreateOrderResult(Guid OrderId);
```

命令（无返回值）：
```csharp
public sealed record DeleteOrderCommand(Guid OrderId) : ICommand;
```

查询：
```csharp
// Queries/GetOrderQuery.cs
public sealed record GetOrderQuery(Guid OrderId) : IQuery<OrderDto>;

public sealed record OrderDto(Guid Id, string CustomerName, decimal Amount);
```

### 3. 创建 FluentValidation 验证器（必须）

每个命令/查询都需要验证器：

```csharp
// Commands/CreateOrderCommandValidator.cs
public sealed class CreateOrderCommandValidator
    : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderCommandValidator()
    {
        RuleFor(x => x.CustomerName)
            .NotEmpty().WithErrorCode(10001);
        RuleFor(x => x.Amount)
            .GreaterThan(0).WithErrorCode(10002);
    }
}
```

### 4. 创建 Handler

Handler 需要注入 `IPipelineContextAccessor` 并传给基类：

```csharp
// Commands/CreateOrderCommandHandler.cs
public sealed class CreateOrderCommandHandler
    : CommandHandler<CreateOrderCommand, CreateOrderResult>
{
    private readonly IRepository<Order, Guid> _orderRepo;

    public CreateOrderCommandHandler(
        IPipelineContextAccessor accessor,
        IRepository<Order, Guid> orderRepo)
        : base(accessor)
    {
        _orderRepo = orderRepo;
    }

    public override async Task<CreateOrderResult> Handle(
        CreateOrderCommand request,
        CancellationToken cancellationToken)
    {
        var order = new Order(request.CustomerName, request.Amount);
        await _orderRepo.InsertAsync(order);
        await _orderRepo.UnitOfWork.CommitAsync(cancellationToken);

        return new CreateOrderResult(order.Id);
    }
}
```

无返回值命令用 `NullableCommandHandler<T>`：

```csharp
public sealed class DeleteOrderCommandHandler
    : NullableCommandHandler<DeleteOrderCommand>
{
    private readonly IRepository<Order, Guid> _orderRepo;

    public DeleteOrderCommandHandler(
        IPipelineContextAccessor accessor,
        IRepository<Order, Guid> orderRepo)
        : base(accessor)
    {
        _orderRepo = orderRepo;
    }

    public override async Task Execute(
        DeleteOrderCommand request,
        CancellationToken cancellationToken)
    {
        var order = await _orderRepo.GetAsync(request.OrderId);
        await _orderRepo.DeleteAsync(order!);
        await _orderRepo.UnitOfWork.CommitAsync(cancellationToken);
    }
}
```

### 5. 在 DI 中注册（如未全局注册 CQRS）

```csharp
builder.Services.AddRaindropCQRS(
    applicationAssemblies: new[] { typeof(Program).Assembly })
    .AddValidation();
```

`AddRaindropCQRS` 会自动扫描所有 Handler/Validator，无需手动逐个注册。

### 6. 在 Minimal API 或 Controller 中使用

```csharp
// Minimal API
app.MapPost("/orders", async (
    CreateOrderCommand cmd,
    ICommandBus bus,
    CancellationToken ct) =>
{
    var result = await bus.Send(cmd, ct);
    return Results.Created($"/orders/{result.OrderId}", result);
});

app.MapGet("/orders/{id:guid}", async (
    Guid id,
    IQueryBus bus,
    CancellationToken ct) =>
{
    var order = await bus.Send(new GetOrderQuery(id), ct);
    return order is not null ? Results.Ok(order) : Results.NotFound();
});
```

### 7. 编写单元测试

```csharp
// 测试 Handler（使用 mock 仓储）
[Fact]
public async Task CreateOrder_ValidCommand_ReturnsResult()
{
    var repo = new Mock<IRepository<Order, Guid>>();
    var accessor = new Mock<IPipelineContextAccessor>();
    var handler = new CreateOrderCommandHandler(accessor.Object, repo.Object);

    var result = await handler.Handle(
        new CreateOrderCommand("Test", 100), CancellationToken.None);

    Assert.NotNull(result);
    Assert.NotEqual(Guid.Empty, result.OrderId);
}

// 测试 Validator
[Fact]
public void Validate_MissingCustomerName_Fails()
{
    var validator = new CreateOrderCommandValidator();
    var result = validator.Validate(
        new CreateOrderCommand("", 100));
    Assert.False(result.IsValid);
}
```

## 检查清单

- [ ] 命令/查询 Record 实现了正确的接口（`ICommand` / `ICommand<T>` / `IQuery<T>`）
- [ ] 创建了 FluentValidation 验证器
- [ ] Handler 注入了 `IPipelineContextAccessor` 并传给基类
- [ ] 写操作 Handler 中调用了 `UnitOfWork.CommitAsync()`
- [ ] 业务异常使用 `FriendlyException` 抛出
- [ ] 文档注释使用中文
- [ ] 编写了单元测试

## 注意事项

- **Handler 构造器必须接收 `IPipelineContextAccessor`**，并传递给基类构造器
- **不要直接在 Handler 中控制事务**，通过 `UnitOfWork.CommitAsync()` 提交
- **不要直接使用 `IMediator`**，用 `ICommandBus` / `IQueryBus` 代替
- **`InsertAndGetIdAsync`** 是设计特例，会立即 SaveChanges，不触发领域事件递归处理。大多数场景用 `InsertAsync` + `CommitAsync`
