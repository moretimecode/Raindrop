using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Raindrop.AspNetCore;
using Raindrop.AspNetCore.Filters;
using Raindrop.AspNetCore.Middlewares;
using Raindrop.Observability.Abstractions;
using Raindrop.Utility;

namespace Raindrop.AspNetCore.Tests;

public class ApiResponseTests
{
    // ===================================================================
    //  ApiResponse 工厂
    // ===================================================================

    [Fact]
    public void ApiResponse_Success_UsesStableJsonShape()
    {
        var response = ApiResponse.Success(new { id = 1 }, ApiMessages.Success);
        var json = JsonSerializer.Serialize(response, RaindropJsonOptions.Default);
        var root = JsonNode.Parse(json)!;

        Assert.Equal(200, (int)root["code"]!);
        Assert.Equal("success", (string)root["message"]!);
        Assert.True((bool)root["success"]!);
        Assert.Equal(1, (int)root["data"]!["id"]!);
    }

    [Fact]
    public void ApiResponse_Success_PreservesGenericDataType()
    {
        var response = ApiResponse.Success(new OrderDto(1));

        Assert.IsType<ApiResponse<OrderDto>>(response);
        Assert.Equal(1, response.Data!.Id);
    }

    [Fact]
    public void ApiResponse_Fail_ReturnsCorrectShape()
    {
        var response = ApiResponse.Fail(400, "参数错误");
        var json = JsonSerializer.Serialize(response, RaindropJsonOptions.Default);
        var root = JsonNode.Parse(json)!;

        Assert.Equal(400, (int)root["code"]!);
        Assert.Equal("参数错误", (string)root["message"]!);
        Assert.False((bool)root["success"]!);
    }

    // ===================================================================
    //  ApiMessages 常量
    // ===================================================================

    [Fact]
    public void ApiMessages_AllMessagesAreNonEmpty()
    {
        Assert.NotEmpty(ApiMessages.Success);
        Assert.NotEmpty(ApiMessages.InternalError);
        Assert.NotEmpty(ApiMessages.BadRequest);
        Assert.NotEmpty(ApiMessages.Unauthorized);
        Assert.NotEmpty(ApiMessages.Forbidden);
        Assert.NotEmpty(ApiMessages.NotFound);
        Assert.NotEmpty(ApiMessages.RequestCanceled);
    }

    // ===================================================================
    //  ExceptionMiddleware
    // ===================================================================

    [Fact]
    public async Task ExceptionMiddleware_FriendlyException_ReturnsBusinessCode()
    {
        var middleware = new RaindropExceptionHandlingMiddleware(
            _ => throw new FriendlyException("余额不足", 10001),
            NullLogger<RaindropExceptionHandlingMiddleware>.Instance);

        var context = CreateHttpContext();
        await middleware.InvokeAsync(context);

        var root = await ParseResponseAsync(context);
        Assert.Equal((int)HttpStatusCode.BadRequest, context.Response.StatusCode);
        Assert.Equal(10001, root["code"]!.GetValue<int>());
        Assert.Equal("余额不足", root["message"]!.GetValue<string>());
        Assert.False(root["success"]!.GetValue<bool>());
    }

    [Fact]
    public async Task ExceptionMiddleware_UnknownException_UsesFriendlyMessage()
    {
        var middleware = new RaindropExceptionHandlingMiddleware(
            _ => throw new InvalidOperationException("boom"),
            NullLogger<RaindropExceptionHandlingMiddleware>.Instance);

        var context = CreateHttpContext();
        await middleware.InvokeAsync(context);

        var root = await ParseResponseAsync(context);
        Assert.Equal((int)HttpStatusCode.InternalServerError, context.Response.StatusCode);
        Assert.Equal(500, root["code"]!.GetValue<int>());
        Assert.Equal(ApiMessages.InternalError, root["message"]!.GetValue<string>());
    }

    [Fact]
    public async Task ExceptionMiddleware_UnauthorizedAccessException_ReturnsForbidden()
    {
        var middleware = new RaindropExceptionHandlingMiddleware(
            _ => throw new UnauthorizedAccessException("nope"),
            NullLogger<RaindropExceptionHandlingMiddleware>.Instance);

        var context = CreateHttpContext();
        await middleware.InvokeAsync(context);

        var root = await ParseResponseAsync(context);
        Assert.Equal((int)HttpStatusCode.Forbidden, context.Response.StatusCode);
        Assert.Equal(403, root["code"]!.GetValue<int>());
        Assert.Equal(ApiMessages.Forbidden, root["message"]!.GetValue<string>());
    }

    [Fact]
    public async Task ExceptionMiddleware_OperationCanceledException_Returns499()
    {
        var middleware = new RaindropExceptionHandlingMiddleware(
            _ => throw new OperationCanceledException(),
            NullLogger<RaindropExceptionHandlingMiddleware>.Instance);

        var context = CreateHttpContext();
        await middleware.InvokeAsync(context);

        var root = await ParseResponseAsync(context);
        Assert.Equal(499, context.Response.StatusCode);
        Assert.Equal(499, root["code"]!.GetValue<int>());
        Assert.Equal(ApiMessages.RequestCanceled, root["message"]!.GetValue<string>());
    }

    [Fact]
    public async Task ExceptionMiddleware_ArgumentException_ReturnsBadRequest()
    {
        var middleware = new RaindropExceptionHandlingMiddleware(
            _ => throw new ArgumentException("invalid"),
            NullLogger<RaindropExceptionHandlingMiddleware>.Instance);

        var context = CreateHttpContext();
        await middleware.InvokeAsync(context);

        var root = await ParseResponseAsync(context);
        Assert.Equal((int)HttpStatusCode.BadRequest, context.Response.StatusCode);
        Assert.Equal(400, root["code"]!.GetValue<int>());
        Assert.Equal(ApiMessages.BadRequest, root["message"]!.GetValue<string>());
    }

    [Fact]
    public async Task ExceptionMiddleware_PreservesExistingHeadersWhenWrapping()
    {
        var middleware = new RaindropExceptionHandlingMiddleware(
            ctx =>
            {
                ctx.Response.Headers["X-Business-Header"] = "keep";
                throw new InvalidOperationException("boom");
            },
            NullLogger<RaindropExceptionHandlingMiddleware>.Instance);

        var context = CreateHttpContext();
        await middleware.InvokeAsync(context);

        var root = await ParseResponseAsync(context);
        Assert.Equal(500, context.Response.StatusCode);
        Assert.Equal("keep", context.Response.Headers["X-Business-Header"].ToString());
        Assert.Equal(500, root["code"]!.GetValue<int>());
    }

    // ===================================================================
    //  StatusCodeMiddleware
    // ===================================================================

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    public async Task StatusCodeMiddleware_WrapsCommonHttpErrors(int statusCode)
    {
        var middleware = new StatusCodeResponseMiddleware(ctx =>
        {
            ctx.Response.StatusCode = statusCode;
            return Task.CompletedTask;
        });

        var context = CreateHttpContext();
        await middleware.InvokeAsync(context);

        var root = await ParseResponseAsync(context);
        Assert.Equal(statusCode, context.Response.StatusCode);
        Assert.Equal(statusCode, root["code"]!.GetValue<int>());
        Assert.False(root["success"]!.GetValue<bool>());
    }

    [Fact]
    public async Task StatusCodeMiddleware_DoesNotWrap200()
    {
        var middleware = new StatusCodeResponseMiddleware(ctx =>
        {
            ctx.Response.StatusCode = 200;
            return Task.CompletedTask;
        });

        var context = CreateHttpContext();
        await middleware.InvokeAsync(context);

        Assert.Equal(200, context.Response.StatusCode);
        Assert.Equal(0, context.Response.Body.Length);
    }

    [Fact]
    public async Task StatusCodeMiddleware_ClearsExistingBodyAndWraps()
    {
        var middleware = new StatusCodeResponseMiddleware(async ctx =>
        {
            ctx.Response.StatusCode = 404;
            await ctx.Response.WriteAsync("default 404 page");
        });

        var context = CreateHttpContext();
        await middleware.InvokeAsync(context);

        var root = await ParseResponseAsync(context);
        Assert.Equal(404, context.Response.StatusCode);
        Assert.Equal(404, root["code"]!.GetValue<int>());
    }

    [Fact]
    public async Task StatusCodeMiddleware_PreservesAuthenticateHeaderWhenWrapping401()
    {
        var middleware = new StatusCodeResponseMiddleware(ctx =>
        {
            ctx.Response.StatusCode = 401;
            ctx.Response.Headers.WWWAuthenticate = "Bearer";
            return Task.CompletedTask;
        });

        var context = CreateHttpContext();
        await middleware.InvokeAsync(context);

        var root = await ParseResponseAsync(context);
        Assert.Equal(401, context.Response.StatusCode);
        Assert.Equal("Bearer", context.Response.Headers.WWWAuthenticate.ToString());
        Assert.Equal(401, root["code"]!.GetValue<int>());
    }

    [Fact]
    public async Task StatusCodeMiddleware_PreservesExistingHeadersWhenWrapping()
    {
        var middleware = new StatusCodeResponseMiddleware(ctx =>
        {
            ctx.Response.StatusCode = 404;
            ctx.Response.Headers.AccessControlAllowOrigin = "https://example.com";
            ctx.Response.Headers.CacheControl = "no-store";
            ctx.Response.Headers["X-Business-Header"] = "keep";
            return Task.CompletedTask;
        });

        var context = CreateHttpContext();
        await middleware.InvokeAsync(context);

        var root = await ParseResponseAsync(context);
        Assert.Equal(404, context.Response.StatusCode);
        Assert.Equal("https://example.com", context.Response.Headers.AccessControlAllowOrigin.ToString());
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
        Assert.Equal("keep", context.Response.Headers["X-Business-Header"].ToString());
        Assert.Equal(404, root["code"]!.GetValue<int>());
    }

    // ===================================================================
    //  ApiResponseResultFilter
    // ===================================================================

    [Fact]
    public void ResultFilter_WrapsObjectResult()
    {
        var filter = new ApiResponseResultFilter();
        var httpContext = new DefaultHttpContext();
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var ctx = new ResultExecutingContext(actionContext, new List<IFilterMetadata>(),
            new ObjectResult(new { name = "raindrop" }), controller: new object());

        filter.OnResultExecuting(ctx);

        var objectResult = Assert.IsType<ObjectResult>(ctx.Result);
        var responseType = objectResult.Value!.GetType();
        Assert.True(responseType.IsGenericType);
        Assert.Equal(typeof(ApiResponse<>), responseType.GetGenericTypeDefinition());
        Assert.Equal(200, responseType.GetProperty("Code")!.GetValue(objectResult.Value));
        Assert.True((bool)responseType.GetProperty("Success")!.GetValue(objectResult.Value)!);
    }

    [Fact]
    public void ResultFilter_UsesDeclaredTypeWhenWrappingObjectResult()
    {
        var filter = new ApiResponseResultFilter();
        var httpContext = new DefaultHttpContext();
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var ctx = new ResultExecutingContext(actionContext, new List<IFilterMetadata>(),
            new ObjectResult(new OrderDto(1)) { DeclaredType = typeof(OrderDto) }, controller: new object());

        filter.OnResultExecuting(ctx);

        var objectResult = Assert.IsType<ObjectResult>(ctx.Result);
        Assert.IsType<ApiResponse<OrderDto>>(objectResult.Value);
    }

    [Fact]
    public void ResultFilter_DoesNotDoubleWrap()
    {
        var filter = new ApiResponseResultFilter();
        var alreadyWrapped = ApiResponse.Success(new { id = 1 });
        var httpContext = new DefaultHttpContext();
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var ctx = new ResultExecutingContext(actionContext, new List<IFilterMetadata>(),
            new ObjectResult(alreadyWrapped), controller: new object());

        filter.OnResultExecuting(ctx);

        var objectResult = Assert.IsType<ObjectResult>(ctx.Result);
        Assert.Same(alreadyWrapped, objectResult.Value);
    }

    // ===================================================================
    //  Helpers
    // ===================================================================


    private static DefaultHttpContext CreateHttpContext()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<JsonNode> ParseResponseAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        var body = await reader.ReadToEndAsync();
        return JsonNode.Parse(body)!;
    }

    private static (ActionExecutingContext, ActionExecutedContext) CreateFilterContexts(
        Dictionary<string, object?> actionArgs,
        ObjectResult? result)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());

        var executing = new ActionExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            actionArgs,
            controller: new object());

        var executed = new ActionExecutedContext(
            actionContext,
            new List<IFilterMetadata>(),
            controller: new object())
        {
            Result = result
        };

        return (executing, executed);
    }

    private sealed record OrderDto(int Id);

    private sealed class CapturingAccessLogWriter : IAccessLogWriter
    {
        public List<AccessLogEntry> Entries { get; } = [];

        public ValueTask WriteAsync(AccessLogEntry entry, CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            return ValueTask.CompletedTask;
        }
    }
}
