# Raindrop.CQRS 设计说明

## 概述

`Raindrop.CQRS` 是基于 MediatR 的 CQRS（命令查询职责分离）轻量级封装，提供：

- **命令与查询分离**：`ICommand<TResponse>` 对写操作建模，`IQuery<TResponse>` 对读操作建模
- **Bus 抽象**：`ICommandBus` / `IQueryBus` 屏蔽 MediatR 原生 API，表达更清晰的语义边界
- **分层 Pipeline Behavior**：`CommandPipelineBehavior` / `QueryPipelineBehavior` / `GlobalPipelineBehavior`，按命令/查询/全局作用域注册行为
- **管道上下文**：`PipelineContext` 在 Behavior 之间传递 Request / Response 及自定义键值数据
- **可空响应**：`NullableResponse` 为无返回值命令提供占位类型，避免 `Unit` 语义模糊
- **诊断集成**：内置 `DiagnosticBehavior`，基于 `ActivitySource` 输出 OpenTelemetry 追踪
- **FluentValidation 集成**：`Raindrop.CQRS.FluentValidation` 以 Pipeline Behavior 方式自动校验请求

## 依赖关系

```text
Raindrop.CQRS
  ├─ MediatR（内嵌源码，12.5.0）
  ├─ Raindrop.Utility（FriendlyException 等）
  └─ Microsoft.Extensions.DependencyInjection.Abstractions

Raindrop.CQRS.FluentValidation
  ├─ Raindrop.CQRS
  ├─ FluentValidation
  └─ Microsoft.Extensions.Options
```

## 命令（Command）

命令代表一次意图明确的写操作。遵循 CQRS 惯例：一个 Command 对应一个 Handler。

### 无返回值命令

```csharp
public sealed record CreateOrderCommand(string CustomerName, decimal Amount) : ICommand;

public sealed class CreateOrderCommandHandler : CommandHandler<CreateOrderCommand>
{
    public override Task<NullableResponse> Handle(CreateOrderCommand request, CancellationToken cancellationToken)
    {
        // ... 业务逻辑
        return Task.FromResult(NullableResponse.Value);
    }
}
```

### 有返回值命令

```csharp
public sealed record PlaceOrderCommand(Guid OrderId) : ICommand<OrderResult>;

public sealed record OrderResult(Guid OrderId, string Status);

public sealed class PlaceOrderCommandHandler : CommandHandler<PlaceOrderCommand, OrderResult>
{
    public override Task<OrderResult> Handle(PlaceOrderCommand request, CancellationToken cancellationToken)
    {
        var result = new OrderResult(request.OrderId, "Placed");
        return Task.FromResult(result);
    }
}
```

### NullableCommandHandler（无返回值便捷基类）

```csharp
public sealed class DeleteOrderCommandHandler : NullableCommandHandler<DeleteOrderCommand>
{
    public override async Task Execute(DeleteOrderCommand request, CancellationToken cancellationToken)
    {
        await _repository.DeleteAsync(request.OrderId);
    }
}
```

### 命令接口定义

```csharp
// 无返回值命令 — 底层是 ICommand<NullableResponse>
public interface ICommand : ICommand<NullableResponse> { }

// 有返回值命令 — 直接扩展 MediatR 的 IRequest<TResponse>
public interface ICommand<out TResponse> : IRequest<TResponse> { }
```

## 查询（Query）

查询代表一次读操作。与命令的语义区别：查询不应该修改系统状态。

```csharp
public sealed record GetOrderQuery(Guid OrderId) : IQuery<OrderDto>;

public sealed record OrderDto(Guid Id, string CustomerName, decimal Amount);

public sealed class GetOrderQueryHandler : QueryHandler<GetOrderQuery, OrderDto>
{
    public override Task<OrderDto> Handle(GetOrderQuery request, CancellationToken cancellationToken)
    {
        // ... 只读查询
    }
}
```

### 查询接口定义

```csharp
public interface IQuery<out TResponse> : IRequest<TResponse> { }
```

## Bus（命令/查询总线）

Bus 是业务代码的入口，内部委托给 `IMediator`。每次调用时自动清理 `PipelineContext`，确保上下文隔离。

```csharp
public interface ICommandBus
{
    Task Send<TCommand>(TCommand command, CancellationToken cancellationToken = default)
        where TCommand : ICommand;

    Task<TResponse> Send<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default);
}

public interface IQueryBus
{
    Task<TResponse> Send<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default);
}
```

### 使用示例

```csharp
// Controller 或 Minimal API 中
app.MapPost("/orders", async (CreateOrderCommand cmd, ICommandBus bus) =>
{
    await bus.Send(cmd);
    return Results.Ok();
});

app.MapGet("/orders/{id}", async (Guid id, IQueryBus bus) =>
{
    var order = await bus.Send(new GetOrderQuery(id));
    return Results.Ok(order);
});
```

## Pipeline 行为（Behaviors）

### 分层设计

MediatR 的 `IPipelineBehavior<TRequest, TResponse>` 对任何请求都会执行。Raindrop 提供三层约束：

| 层次 | 基类 | 约束 | 适用场景 |
|------|------|------|----------|
| **Command** | `CommandPipelineBehavior<TRequest, TResponse>` | `TRequest : ICommand<TResponse>` | UnitOfWork、命令审计 |
| **Query** | `QueryPipelineBehavior<TRequest, TResponse>` | `TRequest : IQuery<TResponse>` | 查询缓存、只读事务 |
| **Global** | `GlobalPipelineBehavior<TRequest, TResponse>` | `TRequest : class` | 诊断、验证、日志 |

所有三层继承自 `AbstractPipelineBehavior<TRequest, TResponse>`，它在 `Handle` 方法中自动捕获 Request / Response 到 `PipelineContext`。

### 自定义 Behavior

```csharp
// 命令级：只对 ICommand<> 生效
public sealed class CommandAuditBehavior<TRequest, TResponse>
    : CommandPipelineBehavior<TRequest, TResponse>
    where TRequest : class, ICommand<TResponse>
{
    public CommandAuditBehavior(IPipelineContextAccessor accessor) : base(accessor) { }

    public override async Task<TResponse> InvokeAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var response = await next();
        _logger.LogInformation("Command {Name} completed in {Ms}ms",
            typeof(TRequest).Name, sw.ElapsedMilliseconds);
        return response;
    }
}
```

### 内置 DiagnosticBehavior

所有请求自动启用，基于 `ActivitySource` 为每个请求创建一个 span。名称格式为请求类型名（如 `CreateOrderCommand`）。

```csharp
// 自动注册，无需手动配置
builder.AddGenericPipeline(typeof(DiagnosticBehavior<,>));
```

## PipelineContext

管道上下文在 Behavior 链中传递数据，作用域为一次 Bus 调用。

```csharp
public class PipelineContext
{
    public object? Request { get; set; }      // 当前请求对象
    public object? Response { get; set; }     // 当前响应对象
    public IReadOnlyDictionary<object, object?> Data { get; }  // 自定义键值数据

    public void SetData(object key, object value, bool isOverWrite = false);
    public object? GetData(object key);
    public void SetRequest(object request);
    public void SetResponse(object response);
    public void Clear();
}
```

### 在 Handler 中访问

```csharp
public sealed class MyHandler : CommandHandler<MyCommand, MyResult>
{
    public MyHandler(IPipelineContextAccessor accessor) : base(accessor) { }

    public override Task<MyResult> Handle(MyCommand request, CancellationToken cancellationToken)
    {
        // Context 由 HandlerContext 基类注入
        Context.SetData("userId", 123);
        // ...
    }
}
```

### 生命周期

- 每次 `Bus.Send()` 调用开始时，`PipelineContext` 被清空。
- `AbstractPipelineBehavior.Handle` 在进入时调用 `SetRequest`，在返回时调用 `SetResponse`。
- Behavior 之间通过 `Context.Data` 传递附加信息。
- 调用结束后 Context 进入下一轮复用。

## DI 注册

### 核心注册

```csharp
// 在 Web 或 Host 项目中注册
services.AddRaindropCQRS(
    applicationAssemblies: new[] { typeof(Program).Assembly });
```

`AddRaindropCQRS` 内部完成：

1. `services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblies(...))` — 注册所有 Handler/IRequest
2. 注册 `ICommandBus` / `IQueryBus`（Scoped）
3. 注册 `IPipelineContextAccessor`（Scoped）
4. 注册 `DiagnosticBehavior` 为全局 Pipeline
5. 返回 `IRaindropCQRSBuilder` 供后续配置

### 注册额外 Behavior

```csharp
services.AddRaindropCQRS(assemblies)
    .AddGenericPipeline(typeof(ValidationBehavior<,>))
    .AddGenericPipeline(typeof(LoggingBehavior<,>));
```

## FluentValidation 集成

### 注册

```csharp
services.AddRaindropCQRS(assemblies)
    .AddValidation(options =>
    {
        options.DefaultErrorCode = 400;
        options.ErrorMessageMaxLength = 200;
    });
```

### 验证行为

`ValidatorBehavior<TRequest, TResponse>` 作为全局 Pipeline Behavior，自动对所有请求执行：

1. 从 DI 获取所有 `IValidator<TRequest>` 实现
2. 调用 `ValidateAsync(request, options => options.IncludeAllRuleSets(), cancellationToken)`，支持 `MustAsync` / `CustomAsync` 等异步规则
3. 收集所有 Error
4. 交给 `IValidationFailureHandler` 统一处理

### 默认失败处理

`DefaultValidationFailureHandler` 将所有错误消息用分号拼接，然后抛出 `FriendlyException`：

```csharp
throw new FriendlyException(
    $"Invalid arguments: {errorMessage}",
    errorCode);
```

其中 `errorMessage` 服从 `ErrorMessageMaxLength` 截断；`errorCode` 取第一条 ValidationFailure 的 `ErrorCode`，若无法解析则回退到 `DefaultErrorCode`。`DefaultErrorCode` 默认值为 400。

### 自定义失败处理

实现 `IValidationFailureHandler` 替换默认行为：

```csharp
public interface IValidationFailureHandler
{
    void Handle(List<ValidationFailure> errors);
}
```

### 验证规则定义

```csharp
public sealed class CreateOrderCommandValidator : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderCommandValidator()
    {
        RuleFor(x => x.CustomerName).NotEmpty().WithErrorCode(10001);
        RuleFor(x => x.Amount).GreaterThan(0).WithErrorCode(10002);
    }
}
```

## 与工作单元的配合

Raindrop 已有完整的 `IUnitOfWork` / `IUnitOfWorkFactory` 体系。如果需要命令执行时自动开启事务，建议在外层（HTTP Filter 或 Minimal API EndpointFilter）通过 `[UnitOfWork]` 特性或 endpoint filter 实现，而不是在 MediatR Pipeline Behavior 中重复处理。

当前 CQRS 模块**不内置 UnitOfWorkBehavior**，遵循“一个模块只做一件事”原则。

## 程序集边界

```text
Raindrop.Utility
  └─ FriendlyException

Raindrop.CQRS
  ├─ Commands / Queries / Bus
  ├─ Behaviors（Abstract / Command / Query / Global / Diagnostic）
  ├─ Context（PipelineContext / IPipelineContextAccessor）
  ├─ Builder（IRaindropCQRSBuilder）
  ├─ NullableResponse
  └─ ServiceCollectionExtensions

Raindrop.CQRS.FluentValidation
  ├─ ValidatorBehavior
  ├─ IValidationFailureHandler / DefaultValidationFailureHandler
  ├─ CQRSValidationOptions
  └─ ServiceCollectionExtensions
```

## 设计决策

| 决策 | 理由 |
|------|------|
| 内嵌 MediatR 而非 NuGet | 版本可控，避免与其他包冲突；Raindrop 已内嵌 12.5.0 源码 |
| `NullableResponse` 替代 `Unit` | `Unit` 是 void 的泛型替代，语义模糊；`NullableResponse` 表达“命令可能返回空” |
| Bus 包装 MediatR | `ICommandBus` / `IQueryBus` 比 `IMediator.Send()` 语义更明确，且自动清理 Context |
| 不在 Behaviors 中做 UnitOfWork | Raindrop 的 UoW 体系更完整，CQRS 模块只做命令/查询分离 |
| 手动扫描注册 IValidator | 避免引入 Scrutor，保持依赖最小化 |
| 诊断行为不手动覆盖 `Activity.Current` | 由 `ActivitySource.StartActivity` 维护父子关系，避免没有 listener 时清空外层 trace |
| `FriendlyException` 两参数 | `message` 已包含完整的业务错误描述，`code` 承载错误码，无需第三个参数 |

## 快速开始示例

## 快速开始示例

### 1. 注册服务

```csharp
// Program.cs — 在 Web 项目中注册
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRaindropCQRS(
    applicationAssemblies: new[] { typeof(Program).Assembly })
    .AddValidation(options =>
    {
        options.DefaultErrorCode = 400;
        options.ErrorMessageMaxLength = 200;
    });
```

### 2. 定义命令和验证器

```csharp
// Commands/CreateOrder.cs
public sealed record CreateOrderCommand(
    string CustomerName,
    decimal Amount) : ICommand<CreateOrderResult>;

public sealed record CreateOrderResult(Guid OrderId);

// Commands/CreateOrderValidator.cs
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

// Commands/CreateOrderHandler.cs
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

### 3. 定义查询

```csharp
// Queries/GetOrder.cs
public sealed record GetOrderQuery(Guid OrderId) : IQuery<OrderDto>;

public sealed record OrderDto(
    Guid Id,
    string CustomerName,
    decimal Amount,
    string Status);

// Queries/GetOrderHandler.cs
public sealed class GetOrderQueryHandler
    : QueryHandler<GetOrderQuery, OrderDto>
{
    private readonly IRepository<Order, Guid> _orderRepo;

    public GetOrderQueryHandler(
        IPipelineContextAccessor accessor,
        IRepository<Order, Guid> orderRepo)
        : base(accessor)
    {
        _orderRepo = orderRepo;
    }

    public override async Task<OrderDto> Handle(
        GetOrderQuery request,
        CancellationToken cancellationToken)
    {
        var order = await _orderRepo.GetAsync(request.OrderId);

        return new OrderDto(
            order!.Id,
            order.CustomerName,
            order.Amount,
            order.Status.ToString());
    }
}
```

### 4. 无返回值命令（NullableCommandHandler）

```csharp
public sealed record DeleteOrderCommand(Guid OrderId) : ICommand;

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

### 5. Minimal API 中使用 Bus

```csharp
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
    return order is not null
        ? Results.Ok(order)
        : Results.NotFound();
});

app.MapDelete("/orders/{id:guid}", async (
    Guid id,
    ICommandBus bus,
    CancellationToken ct) =>
{
    await bus.Send(new DeleteOrderCommand(id), ct);
    return Results.NoContent();
});
```

### 6. 自定义 Pipeline Behavior

```csharp
// 记录每个命令的耗时
public sealed class TimingBehavior<TRequest, TResponse>
    : GlobalPipelineBehavior<TRequest, TResponse>
    where TRequest : class
{
    private readonly ILogger<TimingBehavior<TRequest, TResponse>> _logger;

    public TimingBehavior(
        IPipelineContextAccessor accessor,
        ILogger<TimingBehavior<TRequest, TResponse>> logger)
        : base(accessor)
    {
        _logger = logger;
    }

    public override async Task<TResponse> InvokeAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var response = await next(cancellationToken);
        _logger.LogInformation(
            "{RequestName} completed in {Elapsed}ms",
            typeof(TRequest).Name,
            sw.ElapsedMilliseconds);
        return response;
    }
}

// 注册
services.AddRaindropCQRS(assemblies)
    .AddGenericPipeline(typeof(TimingBehavior<,>));
```
