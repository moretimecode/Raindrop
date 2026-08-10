---
name: raindrop-aspnetcore
description: Raindrop ASP.NET Core HTTP 管道。当用户需要统一响应格式、全局异常处理、Access Log、与 CQRS 集成的 Web API 时使用。
---

# Raindrop.AspNetCore — HTTP 管道

## NuGet 包

```
Raindrop.AspNetCore
Raindrop.Observability.Abstractions
```

## 统一响应格式

所有响应自动包装：
```json
{ "code": 200, "message": "success", "data": {}, "success": true }
```

- 成功：`code=200, success=true`
- `FriendlyException`：`code=业务码, message=异常消息, success=false`
- 未处理异常：`code=HTTP状态码, message=友好提示, success=false`
- 文件流、已包装的 `ApiResponse<T>` 不重复包装

## 异常映射

| 异常 | HTTP | code | 日志 |
|------|------|------|------|
| `FriendlyException` | 400 | 业务码/400 | Warning |
| `OperationCanceledException` | 499 | 499 | Info |
| `UnauthorizedAccessException` | 403 | 403 | Warning |
| `ArgumentException` | 400 | 400 | Warning |
| 未知 | 500 | 500 | Error |

## Access Log

每次请求自动记录：TraceId、Method、Path、StatusCode、耗时、错误信息。

- 成功响应体默认不记录
- 失败响应体自动记录
- MVC Action 入参自动采集

```csharp
// 全局开启成功响应体记录
builder.Services.AddRaindropAspNetCore(logResponseBodyOnSuccess: true);

// 单 Action 覆盖
[AccessLog(Full = true)]
public class OrdersController : ControllerBase { }
```

NLog 按 category `Raindrop.AccessLog` 分流。

## 注册

```csharp
// Program.cs
builder.Services.AddRaindropAspNetCore();  // 默认不记录成功响应体

var app = builder.Build();
app.UseRaindropAspNetCore();  // 中间件：AccessLog → Exception → StatusCode
app.MapControllers();
```

## 请求管道

```
Request
  ├─ AccessLogMiddleware          ← 计时 + 收口日志
  ├─ ExceptionHandlingMiddleware
  │   ├─ StatusCodeResponseMiddleware
  │   │   ├─ [MVC / Minimal API]
  │   │   │   ├─ ICommandBus.Send() / IQueryBus.Send()
  │   │   │   │   ├─ DiagnosticBehavior       ← Activity span
  │   │   │   │   ├─ ValidatorBehavior         ← FluentValidation
  │   │   │   │   └─ Handler
  │   │   │   └─ ApiResponseResultFilter       ← 包装响应
  │   │   └─ 401/403/404 → 统一响应
  │   └─ 异常 → 统一响应
  └─ AccessLogMiddleware          ← 写入最终日志
```

## 完整 Minimal API 示例

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRaindropAspNetCore();
builder.Services.AddRaindropCQRS(new[] { typeof(Program).Assembly })
    .AddValidation();

var app = builder.Build();
app.UseRaindropAspNetCore();

app.MapPost("/orders", async (CreateOrderCommand cmd, ICommandBus bus, CancellationToken ct) =>
{
    var result = await bus.Send(cmd, ct);
    return Results.Created($"/orders/{result.OrderId}", result);
});

app.MapGet("/orders/{id}", async (Guid id, IQueryBus bus, CancellationToken ct) =>
{
    var order = await bus.Send(new GetOrderQuery(id), ct);
    return order is not null ? Results.Ok(order) : Results.NotFound();
});

app.Run();
```

## 与 CQRS 协作

- 业务异常用 `FriendlyException(code, message)` 抛出 → 自动转为统一错误响应
- `ValidatorBehavior` 在 Handler 前校验 → 失败抛 `FriendlyException`
- 响应体由 `ApiResponseResultFilter` 包装，无需手动返回 `ApiResponse<T>`
