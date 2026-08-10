# ASP.NET Core 与可观测性设计

## 目标

`Raindrop.AspNetCore` 负责 HTTP 入站管道能力：

- 统一响应包装。
- 全局异常处理。
- HTTP access log。
- HTTP 请求耗时记录。
- 后续 HTTP `[UnitOfWork]` 自动工作单元入口。

`Raindrop.Observability.Abstractions` 负责跨入口复用的可观测性抽象：

- access log 数据模型。
- access log 写入接口。

HTTP、RabbitMQ、MediatR 等入口都可以写 access log，但它们不互相引用具体实现。

## 类库边界

```text
Raindrop.Observability.Abstractions
  AccessLogEntry
  IAccessLogWriter

Raindrop.AspNetCore
  ApiResponse<T> / ApiResponse
  ApiMessages（常量）
  AddRaindropAspNetCore(logResponseBodyOnSuccess)
  UseRaindropAspNetCore()
  ApiResponseResultFilter（MVC 结果包装）
  AccessLogMiddleware（每次 HTTP 请求最终写入一条 access log）
  AccessLogFilter（IAsyncActionFilter，仅采集 MVC Action 入参和可选响应对象）
  RaindropExceptionHandlingMiddleware
  StatusCodeResponseMiddleware
```

## 统一响应模型

```json
{
  "code": 200,
  "message": "success",
  "data": {},
  "success": true
}
```

默认规则：

- 成功响应：`code = 200`，`success = true`，`message = ApiMessages.Success`。
- `FriendlyException`：`code = FriendlyException.Code`，为 0 时使用 400；`message = exception.Message`，`success = false`。
- 非业务异常：`code = HTTP 状态码`，`message = 友好提示常量`，`success = false`。
- 401、403、404 等非异常状态码也包装为统一响应。

文件、流式响应、已经包装过的 `ApiResponse<T>` 不重复包装。

## 异常处理

| 类型 | HTTP 状态码 | 响应 code | 响应 message | 日志级别 |
|------|-------------|-----------|--------------|----------|
| `FriendlyException` | 400 | `FriendlyException.Code`，为 0 时 400 | 异常消息 | Warning |
| `OperationCanceledException` | 499 | 499 | 请求已取消 | Information |
| `UnauthorizedAccessException` | 403 | 403 | 没有权限访问该资源 | Warning |
| `ArgumentException` | 400 | 400 | 请求参数不正确 | Warning |
| 未知异常 | 500 | 500 | 服务器内部错误 | Error |

`UnauthorizedAccessException` 在 Raindrop 中表示“已进入业务代码但没有权限”，因此映射为 403。未认证的 401 由 ASP.NET Core 认证中间件设置状态码，再由 `StatusCodeResponseMiddleware` 包装响应体。

## 状态码响应包装

401、403、404 如果没有被异常处理中间件捕获，只是由认证、授权、路由或业务代码设置了状态码，则由 `StatusCodeResponseMiddleware` 包装成统一响应。

包装时必须保持原始 HTTP 状态码，并保留已有响应头，例如 CORS、安全头、缓存头、自定义业务头，以及 401 的 `WWW-Authenticate`。`Content-Type` 和 `Content-Length` 由统一响应体重新生成。

## Access Log

Access log 采用 **Middleware 收口 + Filter 采集** 的设计。

`AccessLogMiddleware` 负责最终写入。它只记录一次请求的最终事实：TraceId、Method、Path、最终状态码、耗时、成功/失败、错误信息，并保证 404、401、403、未进入 Controller 的请求也有日志。

`AccessLogFilter` 只负责 MVC Action 的低成本上下文采集：`ActionArguments` 和必要时的 `ObjectResult.Value`。它在 JSON 序列化之前读取对象引用，不拦截响应流，不复制大 JSON。

### 为什么不是 Middleware 直接读取响应流

| | Middleware 读取响应流 | Filter 读取 ObjectResult |
|---|---|---|
| 获取入参 | 只能拿 Path/Query/Body 字符串 | 可拿 `ActionArguments` 强类型 |
| 获取响应 | 需要拦截 Stream，可能双重序列化 | 直接拿 `ObjectResult.Value` 对象引用 |
| 大响应 | 报表、大列表、导出容易撑爆日志 | 默认不记录成功响应体 |
| 覆盖范围 | 适合记录最终状态、耗时 | 适合记录 MVC 业务对象 |

### 记录规则

- 每次请求一定写一条 access log，由 middleware 收口。
- MVC 请求优先记录 `ActionArguments`；未进入 MVC 的请求记录路径和查询信息。
- 耗时一定记录最终请求耗时。
- 状态码一定记录最终 HTTP 状态码。
- 错误信息一定记录异常 message 或状态码对应的友好提示。
- 成功响应体默认不记录。
- 成功响应体仅当全局 `logResponseBodyOnSuccess = true` 或 `[AccessLog(Full = true)]` 时记录。
- 失败响应体记录统一错误响应对象或 MVC 错误结果，便于排查；不通过拦截响应流读取大 body。

这样可以同时满足：

- 日志覆盖所有 HTTP 请求。
- 成功的大 JSON、报表、静态资源不会默认写入日志。
- 失败请求保留完整错误上下文。
- MVC 成功响应体如果需要记录，也是在序列化前拿对象引用，不产生响应流复制成本。

## 注册方式

```csharp
// false = 成功响应默认不记录响应体
builder.Services.AddRaindropAspNetCore();

// true = 成功响应也记录响应体
builder.Services.AddRaindropAspNetCore(logResponseBodyOnSuccess: true);

var app = builder.Build();
app.UseRaindropAspNetCore();
app.MapControllers();
```

## OpenTelemetry

`Raindrop.AspNetCore` 提供 `AddRaindropOpenTelemetry(IConfiguration)` 作为应用入口的统一注册点。
该方法只在读取到有效配置时注册 OpenTelemetry SDK；没有配置遥测平台时直接返回，不影响已有 HTTP 管道、事件总线或 CQRS 埋点代码。

配置结构：

```json
{
  "Raindrop": {
    "OpenTelemetry": {
      "ServiceName": "order-api",
      "ServiceNamespace": "raindrop",
      "ServiceVersion": "1.0.0",
      "ServiceInstanceId": "",
      "AutoGenerateServiceInstanceId": true,
      "OtlpEndpoint": "http://localhost:4317"
    }
  }
}
```

规则：

- `ServiceName` 必填；缺失时视为未配置遥测。
- `OtlpEndpoint` 必填；缺失时不注册导出器，也不启动 OpenTelemetry SDK。
- `ServiceInstanceId` 未填写且 `AutoGenerateServiceInstanceId = true` 时自动生成。
- 环境名写入 `deployment.environment.name`，优先读取 `ASPNETCORE_ENVIRONMENT`，其次 `DOTNET_ENVIRONMENT`，最后默认为 `Production`。
- 配置类为 internal，仅作为 `Raindrop.AspNetCore` 内部绑定模型，不暴露给业务项目。

使用方式：

```csharp
builder.Services.AddRaindropOpenTelemetry(builder.Configuration);
```

`UseRaindropAspNetCore()` 内部顺序固定为：

```text
AccessLogMiddleware
  RaindropExceptionHandlingMiddleware
    StatusCodeResponseMiddleware
```

`AccessLogMiddleware` 必须在最外层，才能记录异常处理和状态码包装之后的最终结果。

不再使用 `Action<RaindropAspNetCoreOptions>? configure` 委托。消息文本使用 `ApiMessages` 静态常量。

## NLog 分流

框架默认通过 `ILogger` 写日志，不直接依赖 NLog。应用可以按 category 分流：

- `Raindrop.AccessLog`：access log 文件。
- 其它 category：normal log 文件。

## 快速开始示例

### 1. 注册服务

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// false（默认）：成功响应不记录响应体
// true：成功响应也记录响应体（调试用）
builder.Services.AddRaindropAspNetCore(logResponseBodyOnSuccess: false);

// 可选：同时注册 CQRS
builder.Services.AddRaindropCQRS(
    applicationAssemblies: new[] { typeof(Program).Assembly })
    .AddValidation();

var app = builder.Build();

// 中间件顺序：AccessLog → Exception → StatusCode
app.UseRaindropAspNetCore();
app.MapControllers();

app.Run();
```

### 2. 统一响应格式

所有响应自动包装为：

```json
{
  "code": 200,
  "message": "success",
  "data": { ... },
  "success": true
}
```

```csharp
// ✅ 正常返回 — 自动包装
[ApiController]
[Route("api/users")]
public class UsersController : ControllerBase
{
    [HttpGet("{id}")]
    public async Task<UserDto> GetById(Guid id)
    {
        var user = await _userService.GetAsync(id);
        return user;  // → { "code": 200, "message": "success", "data": { ... }, "success": true }
    }

    [HttpPost]
    public async Task<UserDto> Create(CreateUserRequest request)
    {
        var user = await _userService.CreateAsync(request);
        return user;
    }
}
```

### 3. 业务异常

```csharp
// 抛出 FriendlyException → 自动转为统一响应
[HttpPost("transfer")]
public async Task Transfer(TransferRequest request)
{
    var balance = await _accountService.GetBalanceAsync(request.FromAccountId);

    if (balance < request.Amount)
    {
        // FriendlyException(code, message)
        throw new FriendlyException("余额不足", 10001);
    }

    await _accountService.TransferAsync(request);
}

// HTTP 响应：400 Bad Request
// Body：{ "code": 10001, "message": "余额不足", "data": null, "success": false }
```

### 4. 异常映射速查

| 抛出异常 | HTTP 状态码 | code | message | 日志级别 |
|----------|-------------|------|---------|----------|
| `new FriendlyException("余额不足", 10001)` | 400 | 10001 | 余额不足 | Warning |
| `new FriendlyException("参数错误")` | 400 | 400 | 参数错误 | Warning |
| `new ArgumentException("...")` | 400 | 400 | 请求参数不正确 | Warning |
| `new UnauthorizedAccessException("...")` | 403 | 403 | 没有权限访问该资源 | Warning |
| `OperationCanceledException` | 499 | 499 | 请求已取消 | Information |
| 其他未处理异常 | 500 | 500 | 服务器内部错误 | Error |

### 5. 访问日志

每次 HTTP 请求自动记录一条 access log，通过 `ILogger` 的 `Raindrop.AccessLog` category 输出。

**默认行为：**
- ✅ 所有请求都记录：TraceId、Method、Path、StatusCode、耗时、错误信息
- ❌ 成功（< 400）响应体**不记录**
- ✅ 失败（≥ 400）响应体记录
- ✅ MVC Action 入参自动采集

**控制响应体记录粒度：**

```csharp
// 方案 A：全局开启成功响应体记录（调试用）
builder.Services.AddRaindropAspNetCore(logResponseBodyOnSuccess: true);

// 方案 B：单个 Controller / Action 覆盖
[AccessLog(Full = true)]  // 成功时也记录响应体
[ApiController]
[Route("api/orders")]
public class OrdersController : ControllerBase
{
    [HttpGet("{id}")]
    public async Task<OrderDto> Get(Guid id) { ... }

    [HttpGet]  // Full = false，成功时不记录
    public async Task<List<OrderDto>> List() { ... }
}
```

### 6. 自定义响应消息

```csharp
// 使用 ApiMessages 常量（当前为中文）
throw new FriendlyException(ApiMessages.BadRequest, 400);
throw new FriendlyException(ApiMessages.NotFound, 404);
throw new FriendlyException(ApiMessages.Forbidden, 403);

// 如需国际化，替换 ApiMessages 常量值或改用资源文件
```

### 7. NLog 分流示例

```xml
<!-- nlog.config -->
<targets>
    <!-- 普通业务日志 -->
    <target name="logfile" xsi:type="File"
            fileName="logs/app-${shortdate}.log" />

    <!-- Access log 独立文件 -->
    <target name="accessLogFile" xsi:type="File"
            fileName="logs/access-${shortdate}.log" />
</targets>

<rules>
    <!-- Rainbow.AccessLog category → access log 文件 -->
    <logger name="Raindrop.AccessLog"
            minlevel="Info"
            writeTo="accessLogFile"
            final="true" />

    <!-- 其他 → 普通文件 -->
    <logger name="*" minlevel="Info" writeTo="logfile" />
</rules>
```

### 8. 完整 Minimal API 示例

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRaindropAspNetCore();
builder.Services.AddRaindropCQRS(
    new[] { typeof(Program).Assembly })
    .AddValidation();

var app = builder.Build();
app.UseRaindropAspNetCore();

// 业务命令
app.MapPost("/orders", async (
    CreateOrderCommand cmd,
    ICommandBus bus,
    CancellationToken ct) =>
{
    var result = await bus.Send(cmd, ct);
    return Results.Created($"/orders/{result.OrderId}", result);
});

// 业务查询
app.MapGet("/orders/{id:guid}", async (
    Guid id,
    IQueryBus bus,
    CancellationToken ct) =>
{
    var order = await bus.Send(new GetOrderQuery(id), ct);
    return Results.Ok(order);
});

// 业务异常直接抛
app.MapPost("/transfer", async (
    TransferCommand cmd,
    ICommandBus bus,
    CancellationToken ct) =>
{
    // 验证逻辑在 ValidatorBehavior 中完成
    // 业务逻辑在 Handler 中抛出 FriendlyException
    await bus.Send(cmd, ct);
    return Results.Ok();
});

app.Run();
```

### 9. 与 CQRS 的协作流程

```text
HTTP Request
  │
  ├─ AccessLogMiddleware          ← 开始计时，拦截异常
  │
  ├─ RaindropExceptionHandlingMiddleware
  │   │
  │   ├─ StatusCodeResponseMiddleware
  │   │   │
  │   │   ├─ [MVC / Minimal API]
  │   │   │   ├─ ICommandBus.Send()  /  IQueryBus.Send()
  │   │   │   │   ├─ DiagnosticBehavior        ← Activity span
  │   │   │   │   ├─ ValidatorBehavior          ← FluentValidation
  │   │   │   │   ├─ [自定义 Behaviors]
  │   │   │   │   └─ CommandHandler / QueryHandler
  │   │   │   │
  │   │   │   └─ ApiResponseResultFilter        ← 包装 ObjectResult
  │   │   │
  │   │   └─ 401/403/404 → 统一响应体
  │   │
  │   └─ 异常 → 统一响应体
  │
  └─ AccessLogMiddleware          ← 写入一条 access log
```
