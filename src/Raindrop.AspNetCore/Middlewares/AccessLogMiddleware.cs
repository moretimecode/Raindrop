using System.Diagnostics;
using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Raindrop.AspNetCore.Logging;
using Raindrop.Observability.Abstractions;

namespace Raindrop.AspNetCore.Middlewares;

/// <summary>
/// HTTP 访问日志中间件。
/// 负责在请求结束后写入一条最终 access log，不拦截响应流。
/// </summary>
public sealed class AccessLogMiddleware(
    RequestDelegate next,
    IAccessLogWriter accessLogWriter,
    ILogger<AccessLogMiddleware> logger)
{
    private readonly RequestDelegate _next = next;
    private readonly IAccessLogWriter _accessLogWriter = accessLogWriter;
    private readonly ILogger<AccessLogMiddleware> _logger = logger;

    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        Exception? exception = null;

        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            exception = ex;
            throw;
        }
        finally
        {
            stopwatch.Stop();
            await WriteAccessLogAsync(context, stopwatch.ElapsedMilliseconds, exception);
        }
    }

    private async ValueTask WriteAccessLogAsync(
        HttpContext context,
        long elapsedMilliseconds,
        Exception? exception)
    {
        try
        {
            var feature = context.Features.Get<AccessLogFeature>();
            var statusCode = context.Response.StatusCode;
            var success = exception is null && statusCode < 400;
            var errorMessage = exception?.Message
                ?? feature?.ErrorMessage
                ?? (success ? null : ResolveMessage(statusCode));

            var entry = new AccessLogEntry(
                context.TraceIdentifier,
                "http",
                $"{context.Request.Method} {context.Request.Path}",
                context.Request.Method,
                context.Request.Path.ToString(),
                feature?.Request ?? CreateFallbackRequest(context),
                feature?.HasResponse == true ? feature.Response : null,
                statusCode,
                elapsedMilliseconds,
                success,
                errorMessage);

            await _accessLogWriter.WriteAsync(entry, context.RequestAborted);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "HTTP access log 写入失败。");
        }
    }

    private static object? CreateFallbackRequest(HttpContext context)
    {
        return context.Request.QueryString.HasValue
            ? new { query = context.Request.QueryString.Value }
            : null;
    }

    private static string ResolveMessage(int statusCode)
    {
        return statusCode switch
        {
            (int)HttpStatusCode.Unauthorized => ApiMessages.Unauthorized,
            (int)HttpStatusCode.Forbidden => ApiMessages.Forbidden,
            (int)HttpStatusCode.NotFound => ApiMessages.NotFound,
            >= 500 => ApiMessages.InternalError,
            >= 400 => ApiMessages.BadRequest,
            _ => string.Empty
        };
    }
}
