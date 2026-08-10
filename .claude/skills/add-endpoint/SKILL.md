---
name: add-endpoint
description: 在 Raindrop 项目中添加 Minimal API 端点或 Controller，配置统一响应包装、异常处理、访问日志和 CQRS 集成。当用户需要新增 HTTP API 接口、配置 ASP.NET Core 管道时使用。触发词：添加 API、新增接口、添加端点、Minimal API、Controller、HTTP 接口、Web API。
---

# 添加 ASP.NET Core 端点

在 Raindrop 项目中添加 HTTP API 端点，利用框架的统一响应包装、异常处理和访问日志。

## 前置知识

参考文档：
- [docs/aspnetcore-observability.md](docs/aspnetcore-observability.md) — ASP.NET Core 与可观测性
- [project-overview](.claude/skills/raindrop-project-overview/SKILL.md) — 项目架构总览
- [add-cqrs-command](.claude/skills/raindrop-add-cqrs-command/SKILL.md) — 添加 CQRS 命令

## 步骤

### 1. 注册 Raindrop ASP.NET Core 服务

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// 注册 Raindrop ASP.NET Core（统一响应 + 异常处理 + 访问日志）
builder.Services.AddRaindropAspNetCore();

// 可选：全局开启成功响应体记录（调试用）
// builder.Services.AddRaindropAspNetCore(logResponseBodyOnSuccess: true);

// 可选：同时注册 CQRS
builder.Services.AddRaindropCQRS(
    new[] { typeof(Program).Assembly })
    .AddValidation();

var app = builder.Build();

// 中间件必须这样注册（顺序固定）
app.UseRaindropAspNetCore();

app.Run();
```

### 2. 中间件执行顺序

`UseRaindropAspNetCore()` 内部顺序固定：

```text
AccessLogMiddleware
  → RaindropExceptionHandlingMiddleware
    → StatusCodeResponseMiddleware
      → [业务代码：Minimal API / Controller]
```

- AccessLogMiddleware 在最外层，记录每次请求的最终结果
- 异常先被 RaindropExceptionHandlingMiddleware 捕获
- 非异常的 401/403/404 由 StatusCodeResponseMiddleware 包装

### 3. 创建 Minimal API 端点

所有端点自动包装为统一响应格式 `{ code, message, data, success }`：

```csharp
// 写操作端点
app.MapPost("/orders", async (
    CreateOrderCommand cmd,
    ICommandBus bus,
    CancellationToken ct) =>
{
    var result = await bus.Send(cmd, ct);
    return Results.Created($"/orders/{result.OrderId}", result);
});

// 读操作端点
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

// 删除操作端点
app.MapDelete("/orders/{id:guid}", async (
    Guid id,
    ICommandBus bus,
    CancellationToken ct) =>
{
    await bus.Send(new DeleteOrderCommand(id), ct);
    return Results.NoContent();
});
```

### 4. 统一响应格式

所有响应自动包装为：

```json
// 成功
{ "code": 200, "message": "success", "data": { ... }, "success": true }

// 业务异常
{ "code": 10001, "message": "余额不足", "data": null, "success": false }

// 参数错误
{ "code": 400, "message": "请求参数不正确", "data": null, "success": false }
```

### 5. 抛出业务异常

```csharp
app.MapPost("/transfer", async (TransferCommand cmd, ICommandBus bus) =>
{
    // 业务逻辑在 Handler 中...
    await bus.Send(cmd);
    return Results.Ok();
});

// Handler 中抛出业务异常
public class TransferCommandHandler : CommandHandler<TransferCommand, TransferResult>
{
    public override async Task<TransferResult> Handle(
        TransferCommand request,
        CancellationToken cancellationToken)
    {
        var balance = await _accountRepo.GetAsync(request.FromAccountId);

        if (balance!.Amount < request.Amount)
        {
            // FriendlyException(code, message)
            throw new FriendlyException("余额不足", 10001);
        }

        // ... 执行转账
    }
}
```

### 6. 异常 → HTTP 响应映射速查

| 异常 | HTTP 状态码 | code | message | 日志级别 |
|------|-------------|------|---------|----------|
| `new FriendlyException("余额不足", 10001)` | 400 | 10001 | 余额不足 | Warning |
| `new FriendlyException("参数错误")` | 400 | 400 | 参数错误 | Warning |
| `new ArgumentException("...")` | 400 | 400 | 请求参数不正确 | Warning |
| `new UnauthorizedAccessException("...")` | 403 | 403 | 没有权限访问该资源 | Warning |
| `OperationCanceledException` | 499 | 499 | 请求已取消 | Information |
| 其他未处理异常 | 500 | 500 | 服务器内部错误 | Error |

### 7. 控制访问日志粒度

访问日志默认记录 TraceId、Method、Path、StatusCode、耗时、错误信息。成功响应体默认不记录。

```csharp
// 全局开启成功响应体记录
builder.Services.AddRaindropAspNetCore(logResponseBodyOnSuccess: true);

// 单个 Controller / Action 覆盖
[AccessLog(Full = true)]  // 成功时也记录响应体
[ApiController]
[Route("api/orders")]
public class OrdersController : ControllerBase
{
    [HttpGet("{id}")]
    public async Task<OrderDto> Get(Guid id) { ... }

    [HttpGet]  // Full = false（默认），成功时不记录响应体
    public async Task<List<OrderDto>> List() { ... }
}
```

### 8. NLog 日志分流

```xml
<!-- nlog.config -->
<rules>
    <!-- Access Log 独立文件 -->
    <logger name="Raindrop.AccessLog"
            minlevel="Info"
            writeTo="accessLogFile"
            final="true" />

    <!-- 其他业务日志 -->
    <logger name="*" minlevel="Info" writeTo="logfile" />
</rules>
```

### 9. 使用 Controller（替代 Minimal API）

```csharp
[ApiController]
[Route("api/users")]
public class UsersController : ControllerBase
{
    private readonly ICommandBus _commandBus;
    private readonly IQueryBus _queryBus;

    public UsersController(ICommandBus commandBus, IQueryBus queryBus)
    {
        _commandBus = commandBus;
        _queryBus = queryBus;
    }

    [HttpGet("{id}")]
    public async Task<UserDto> GetById(Guid id)
    {
        return await _queryBus.Send(new GetUserQuery(id));
        // 自动包装：{ "code": 200, "message": "success", "data": { ... }, "success": true }
    }

    [HttpPost]
    public async Task<CreateUserResult> Create(CreateUserCommand cmd)
    {
        return await _commandBus.Send(cmd);
    }
}
```

### 10. 不变包装的场景

以下响应不会被包装：
- 已经是 `ApiResponse<T>` 类型的结果
- 文件/流式响应
- `IResult` 返回类型的流式响应

### 11. 编写集成测试

使用 `Microsoft.AspNetCore.TestHost`：

```csharp
public class OrdersEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public OrdersEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetOrder_NotFound_ReturnsWrapped404()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/orders/00000000-0000-0000-0000-000000000000");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<object>>();
        Assert.False(body!.Success);
        Assert.Equal(404, body.Code);
    }

    [Fact]
    public async Task CreateOrder_ValidCommand_ReturnsCreated()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/orders",
            new CreateOrderCommand("Test", 100));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
```

## 检查清单

- [ ] 注册了 `AddRaindropAspNetCore()`
- [ ] 使用 `UseRaindropAspNetCore()` 代替手动注册中间件
- [ ] 命令端点通过 `ICommandBus.Send()` 调用 Handler
- [ ] 查询端点通过 `IQueryBus.Send()` 调用 Handler
- [ ] 业务异常使用 `FriendlyException(code, message)` 抛出
- [ ] 不手动包装返回值为 `ApiResponse<T>`（框架自动包装）
- [ ] 文档注释使用中文
- [ ] 编写了集成测试

## 常见错误

- ❌ 手动包装 `ApiResponse<T>` 返回 — 框架会自动包装，手动包装会变成双重包装
- ❌ 在 Controller 中直接注入 `IMediator` — 应使用 `ICommandBus` / `IQueryBus`
- ❌ 忘记调用 `UseRaindropAspNetCore()` — 统一响应和异常处理不会生效
- ❌ 使用 `UseExceptionHandler` 替代 Raindrop 的中间件 — 两者会冲突
