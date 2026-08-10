using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Raindrop.AspNetCore.Logging;
using Raindrop.Utility;

namespace Raindrop.AspNetCore.Middlewares;

/// <summary>
/// 全局异常处理中间件。
/// 将未处理异常转换为统一 ApiResponse，并按异常类型写入对应级别的日志。
/// </summary>
public sealed class RaindropExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<RaindropExceptionHandlingMiddleware> logger)
{
    private readonly RequestDelegate _next = next;
    private readonly ILogger<RaindropExceptionHandlingMiddleware> _logger = logger;

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var descriptor = Describe(exception);

        switch (descriptor.LogLevel)
        {
            case LogLevel.Error:
                _logger.LogError(exception, "HTTP 请求处理失败。");
                break;
            case LogLevel.Warning:
                _logger.LogWarning(exception, "HTTP 请求处理异常。");
                break;
            default:
                _logger.LogInformation(exception, "HTTP 请求已取消。");
                break;
        }

        if (!context.Response.HasStarted)
        {
            var headers = ResponseHeaderSnapshot.Capture(context.Response);
            context.Response.Clear();
            context.Response.StatusCode = descriptor.StatusCode;
            headers.Restore(context.Response);
            context.Response.ContentType = "application/json; charset=utf-8";
            var response = ApiResponse.Fail(descriptor.Code, descriptor.Message);
            var feature = context.GetOrCreateAccessLogFeature();
            feature.Response = response;
            feature.HasResponse = true;
            feature.ErrorMessage = exception.Message;

            await context.Response.WriteAsJsonAsync(
                response,
                RaindropJsonOptions.Default);
        }
    }

    private static ExceptionDescriptor Describe(Exception exception)
    {
        return exception switch
        {
            FriendlyException friendlyException => new ExceptionDescriptor(
                (int)HttpStatusCode.BadRequest,
                friendlyException.Code == 0 ? (int)HttpStatusCode.BadRequest : friendlyException.Code,
                friendlyException.Message,
                LogLevel.Warning),
            OperationCanceledException => new ExceptionDescriptor(
                499, 499, ApiMessages.RequestCanceled, LogLevel.Information),
            UnauthorizedAccessException => new ExceptionDescriptor(
                (int)HttpStatusCode.Forbidden,
                (int)HttpStatusCode.Forbidden,
                ApiMessages.Forbidden,
                LogLevel.Warning),
            ArgumentException => new ExceptionDescriptor(
                (int)HttpStatusCode.BadRequest,
                (int)HttpStatusCode.BadRequest,
                ApiMessages.BadRequest,
                LogLevel.Warning),
            _ => new ExceptionDescriptor(
                (int)HttpStatusCode.InternalServerError,
                (int)HttpStatusCode.InternalServerError,
                ApiMessages.InternalError,
                LogLevel.Error)
        };
    }

    private sealed record ExceptionDescriptor(
        int StatusCode,
        int Code,
        string Message,
        LogLevel LogLevel);
}
