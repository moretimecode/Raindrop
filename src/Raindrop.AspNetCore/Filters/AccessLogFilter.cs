using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Raindrop.AspNetCore.Logging;

namespace Raindrop.AspNetCore.Filters;

/// <summary>
/// HTTP 访问日志过滤器。
/// 仅采集 MVC Action 入参和可选响应对象，最终日志由 AccessLogMiddleware 写入。
/// </summary>
public sealed class AccessLogFilter(
    AccessLogFilterConfig config) : IAsyncActionFilter
{
    private readonly bool _globalLogResponseBody = config.LogResponseBodyOnSuccess;

    public async Task OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next)
    {
        var feature = context.HttpContext.GetOrCreateAccessLogFeature();
        feature.Request = context.ActionArguments.Count > 0 ? context.ActionArguments : null;

        var executed = await next();

        if (executed.Exception is not null)
        {
            feature.ErrorMessage = executed.Exception.Message;
            return;
        }

        var statusCode = ResolveStatusCode(context, executed.Result);
        var logResponseBody = ResolveLogResponseBody(context, statusCode);

        if (executed.Result is ObjectResult objectResult && logResponseBody)
        {
            feature.Response = objectResult.Value;
            feature.HasResponse = true;
        }
    }

    private static int ResolveStatusCode(ActionExecutingContext context, IActionResult? result)
    {
        return result switch
        {
            ObjectResult { StatusCode: int statusCode } => statusCode,
            StatusCodeResult statusCodeResult => statusCodeResult.StatusCode,
            _ => context.HttpContext.Response.StatusCode
        };
    }

    private bool ResolveLogResponseBody(ActionExecutingContext context, int statusCode)
    {
        var attr = context.ActionDescriptor.EndpointMetadata
            .OfType<AccessLogAttribute>()
            .LastOrDefault();

        if (attr is not null)
            return statusCode >= 400 || attr.Full;

        return statusCode >= 400 || _globalLogResponseBody;
    }
}
