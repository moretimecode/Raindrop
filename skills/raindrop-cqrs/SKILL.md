---
name: raindrop-cqrs
description: Raindrop CQRS 命令查询职责分离。当用户需要基于 MediatR 的 Command/Query、Bus、Pipeline Behavior、FluentValidation 自动校验时使用。
---

# Raindrop.CQRS — 命令查询职责分离

## NuGet 包

```
Raindrop.CQRS
Raindrop.CQRS.FluentValidation
```

## 命令

```csharp
// 无返回值命令
public sealed record DeleteOrderCommand(Guid OrderId) : ICommand;

// 有返回值命令
public sealed record CreateOrderCommand(string Name, decimal Amount) : ICommand<CreateOrderResult>;
public sealed record CreateOrderResult(Guid OrderId);
```

### 处理器

```csharp
// 有返回值
public sealed class CreateOrderHandler : CommandHandler<CreateOrderCommand, CreateOrderResult>
{
    public CreateOrderHandler(IPipelineContextAccessor accessor) : base(accessor) { }

    public override async Task<CreateOrderResult> Handle(CreateOrderCommand request, CancellationToken ct)
    {
        var order = new Order(request.Name, request.Amount);
        await _repo.InsertAsync(order);
        await _repo.UnitOfWork.CommitAsync(ct);
        return new CreateOrderResult(order.Id);
    }
}

// 无返回值 — 用 NullableCommandHandler
public sealed class DeleteOrderHandler : NullableCommandHandler<DeleteOrderCommand>
{
    public override async Task Execute(DeleteOrderCommand request, CancellationToken ct)
    {
        var order = await _repo.GetAsync(request.OrderId);
        await _repo.DeleteAsync(order!);
        await _repo.UnitOfWork.CommitAsync(ct);
    }
}
```

## 查询

```csharp
public sealed record GetOrderQuery(Guid OrderId) : IQuery<OrderDto>;
public sealed record OrderDto(Guid Id, string Name, decimal Amount);

public sealed class GetOrderHandler : QueryHandler<GetOrderQuery, OrderDto>
{
    public override async Task<OrderDto> Handle(GetOrderQuery request, CancellationToken ct)
    {
        return await _repo.FirstOrDefaultAsync<OrderDto>(o => o.Id == request.Id);
    }
}
```

## Bus — 统一入口

```csharp
ICommandBus  → Send(command)     // 命令
IQueryBus    → Send(query)       // 查询
// 每次调用自动清理 PipelineContext，上下文隔离
```

使用：
```csharp
app.MapPost("/orders", async (CreateOrderCommand cmd, ICommandBus bus, CancellationToken ct) =>
{
    var result = await bus.Send(cmd, ct);
    return Results.Created($"/orders/{result.OrderId}", result);
});

app.MapGet("/orders/{id}", async (Guid id, IQueryBus bus, CancellationToken ct) =>
{
    var order = await bus.Send(new GetOrderQuery(id), ct);
    return Results.Ok(order);
});
```

## Pipeline Behavior — 三层拦截

| 层次 | 基类 | 约束 | 适用场景 |
|------|------|------|----------|
| Command | `CommandPipelineBehavior<T,R>` | `T : ICommand<R>` | UoW、命令审计 |
| Query | `QueryPipelineBehavior<T,R>` | `T : IQuery<R>` | 查询缓存、只读事务 |
| Global | `GlobalPipelineBehavior<T,R>` | `T : class` | 诊断、验证、日志 |

```csharp
// 自定义 Behavior
public sealed class TimingBehavior<TRequest, TResponse>
    : GlobalPipelineBehavior<TRequest, TResponse>
    where TRequest : class
{
    public TimingBehavior(IPipelineContextAccessor accessor) : base(accessor) { }

    public override async Task<TResponse> InvokeAsync(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var response = await next(ct);
        _logger.LogInformation("{Name} in {Ms}ms", typeof(TRequest).Name, sw.ElapsedMilliseconds);
        return response;
    }
}
```

## PipelineContext — 上下文传递

```csharp
// Handler 中通过 Context 属性访问
Context.SetData("userId", 123);
var userId = Context.GetData("userId");
```

作用域：每次 `Bus.Send()` 自动清理。Behavior 间共享，单次调用内有效。

## FluentValidation 集成

```csharp
// 验证器
public sealed class CreateOrderValidator : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithErrorCode(10001);
        RuleFor(x => x.Amount).GreaterThan(0).WithErrorCode(10002);
    }
}
```

验证失败 → `FriendlyException("Invalid arguments: ...", errorCode)`。

### 自定义失败处理

```csharp
public class MyFailureHandler : IValidationFailureHandler
{
    public void Handle(List<ValidationFailure> errors) => throw new MyException(errors);
}
```

## DI 注册

```csharp
services.AddRaindropCQRS(assemblies)
    .AddValidation(options =>
    {
        options.DefaultErrorCode = 400;
        options.ErrorMessageMaxLength = 200;
    })
    .AddGenericPipeline(typeof(TimingBehavior<,>));
// 内置 DiagnosticBehavior 自动注册，基于 ActivitySource 追踪
```

## 设计决策

| 决策 | 理由 |
|------|------|
| MediatR 内嵌源码 12.5.0 | 版本可控 |
| `NullableResponse` 替代 `Unit` | 语义更清晰 |
| Bus 包装 MediatR | 自动清理 Context |
| CQRS 模块不做 UoW | 职责分离 |
