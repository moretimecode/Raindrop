using System.Net;
using Microsoft.AspNetCore.Http;
using Raindrop.AspNetCore.Logging;

namespace Raindrop.AspNetCore.Middlewares;

/// <summary>
/// HTTP 状态码响应包装中间件。
/// 将 401、403、404 等没有抛异常但需要统一响应体的状态码包装为 ApiResponse。
/// </summary>
public sealed class StatusCodeResponseMiddleware(RequestDelegate next)
{
    private readonly RequestDelegate _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        await _next(context);

        if (context.Response.HasStarted || !IsTargetStatusCode(context.Response.StatusCode))
        {
            return;
        }

        var statusCode = context.Response.StatusCode;
        var headers = ResponseHeaderSnapshot.Capture(context.Response);

        // Clear 会把 StatusCode 重置为 200，所以先保存后恢复
        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        headers.Restore(context.Response);

        context.Response.ContentType = "application/json; charset=utf-8";
        var response = ApiResponse.Fail(statusCode, ResolveMessage(statusCode));
        var feature = context.GetOrCreateAccessLogFeature();
        feature.Response = response;
        feature.HasResponse = true;
        feature.ErrorMessage = response.Message;

        await context.Response.WriteAsJsonAsync(
            response,
            RaindropJsonOptions.Default);
    }

    private static bool IsTargetStatusCode(int statusCode)
    {
        return statusCode is (int)HttpStatusCode.Unauthorized
            or (int)HttpStatusCode.Forbidden
            or (int)HttpStatusCode.NotFound;
    }

    private static string ResolveMessage(int statusCode)
    {
        return statusCode switch
        {
            (int)HttpStatusCode.Unauthorized => ApiMessages.Unauthorized,
            (int)HttpStatusCode.Forbidden => ApiMessages.Forbidden,
            (int)HttpStatusCode.NotFound => ApiMessages.NotFound,
            _ => ApiMessages.InternalError
        };
    }
}
