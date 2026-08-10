using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Raindrop.AspNetCore;
using Raindrop.Utility;

namespace Raindrop.AspNetCore.Sample.Scenarios;

/// <summary>
/// 演示 Raindrop.AspNetCore 集成：
///   - 统一响应包装（ApiResponseResultFilter）
///   - 全局异常处理（RaindropExceptionHandlingMiddleware）
///   - 状态码响应包装（StatusCodeResponseMiddleware）
///   - HTTP 访问日志（AccessLogMiddleware + AccessLogFilter）
///   - FriendlyException 业务异常
///   - AccessLog 特性控制响应体记录粒度
/// </summary>
public static class AspNetCoreSampleScenario
{
    public static async Task RunAsync(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // 关闭默认的服务器日志，让我们的 access log 更清晰
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        builder.Logging.SetMinimumLevel(LogLevel.Information);

        // 注册 Raindrop.AspNetCore — 成功响应体默认不记录
        builder.Services.AddRaindropAspNetCore(logResponseBodyOnSuccess: false);

        var app = builder.Build();

        // 注册 Raindrop 中间件（顺序：AccessLog → Exception → StatusCode）
        app.UseRaindropAspNetCore();

        // ─── 1. 正常响应 — 自动包装 ───
        app.MapGet("/api/hello", () =>
        {
            return new { Message = "Hello, Raindrop!", Timestamp = DateTime.UtcNow };
        });

        // ─── 2. 有返回值 — 包装为 ApiResponse<T> ───
        app.MapGet("/api/users/{id:int}", (int id) =>
        {
            return new UserDto(id, $"User_{id}", $"user{id}@example.com");
        });

        // ─── 3. 业务异常 — FriendlyException ───
        app.MapGet("/api/error/business", () =>
        {
            throw new FriendlyException("余额不足，当前余额 50.00，需要 100.00", 10001);
        });

        // ─── 4. 参数错误 ───
        app.MapGet("/api/error/argument", (string? name) =>
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("name 参数不能为空");
            return new { Name = name };
        });

        // ─── 5. 404 — 路由不匹配时由框架设置状态码，StatusCodeMiddleware 包装 ───
        // （访问任意不存在的路径即可测试）

        // ─── 6. 成功响应体记录 — 通过特性控制 ───
        // 注意：Minimal API 中 [AccessLog] 特性不生效，此处演示概念
        app.MapGet("/api/debug/full-log", () =>
        {
            return new { Secret = "这条成功响应的 body 不会被记录（默认行为）" };
        });

        Console.WriteLine("=============================================");
        Console.WriteLine("  Raindrop.AspNetCore 示例已启动");
        Console.WriteLine("=============================================");
        Console.WriteLine();
        Console.WriteLine("  测试端点：");
        Console.WriteLine("    GET /api/hello              → 统一响应包装");
        Console.WriteLine("    GET /api/users/1            → 有返回值包装");
        Console.WriteLine("    GET /api/error/business     → FriendlyException (10001)");
        Console.WriteLine("    GET /api/error/argument     → ArgumentException → 400");
        Console.WriteLine("    GET /api/nonexistent        → 404 状态码包装");
        Console.WriteLine();
        Console.WriteLine("  Access log 输出到控制台（category: Raindrop.AccessLog）");
        Console.WriteLine("=============================================");
        Console.WriteLine();

        await app.RunAsync();
    }

    /// <summary>
    /// 演示用 DTO。
    /// </summary>
    private sealed record UserDto(int Id, string Name, string Email);
}
