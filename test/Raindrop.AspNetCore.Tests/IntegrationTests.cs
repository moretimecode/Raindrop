using System.Net;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Raindrop.Observability.Abstractions;

namespace Raindrop.AspNetCore.Tests;

/// <summary>
/// 端到端集成测试：启动 TestServer，验证 Filter 隐式运行、[AccessLog] 属性、ApiResponse 包装。
/// </summary>
public class IntegrationTests
{
    // ===================================================================
    //  AccessLogFilter 隐式运行（不打标签自动有日志）
    // ===================================================================

    [Fact]
    public async Task AccessLog_WithoutAttribute_LogsRequestButNotResponse()
    {
        var writer = new CapturingAccessLogWriter();
        using var host = await BuildHostAsync(writer, logResponseBodyOnSuccess: false);

        var response = await host.GetTestClient().GetAsync("/api/test/hello");
        response.EnsureSuccessStatusCode();

        var entry = Assert.Single(writer.Entries);
        Assert.Equal("GET /api/test/hello", entry.Operation);
        Assert.Equal(200, entry.StatusCode);
        Assert.True(entry.Success);
        Assert.Null(entry.Response); // 默认不记录响应体
    }

    // ===================================================================
    //  [AccessLog(Full = true)] 记录响应体
    // ===================================================================

    [Fact]
    public async Task AccessLog_WithFullTrue_LogsResponseBody()
    {
        var writer = new CapturingAccessLogWriter();
        using var host = await BuildHostAsync(writer, logResponseBodyOnSuccess: false);

        var response = await host.GetTestClient().GetAsync("/api/test/withFull");
        response.EnsureSuccessStatusCode();

        var entry = Assert.Single(writer.Entries);
        Assert.NotNull(entry.Response);
        Assert.Contains("full response", entry.Response!.ToString());
    }

    [Fact]
    public async Task AccessLog_GlobalFullTrue_LogsSuccessResponseBody()
    {
        var writer = new CapturingAccessLogWriter();
        using var host = await BuildHostAsync(writer, logResponseBodyOnSuccess: true);

        var response = await host.GetTestClient().GetAsync("/api/test/hello");
        response.EnsureSuccessStatusCode();

        var entry = Assert.Single(writer.Entries);
        Assert.NotNull(entry.Response);
        Assert.Contains("hello world", entry.Response!.ToString());
    }

    [Fact]
    public async Task AccessLog_AttributeFullFalse_SuppressesGlobalResponseBody()
    {
        var writer = new CapturingAccessLogWriter();
        using var host = await BuildHostAsync(writer, logResponseBodyOnSuccess: true);

        var response = await host.GetTestClient().GetAsync("/api/test/withoutFull");
        response.EnsureSuccessStatusCode();

        var entry = Assert.Single(writer.Entries);
        Assert.Null(entry.Response);
    }

    // ===================================================================
    //  异常 → 错误信息一定记录
    // ===================================================================

    [Fact]
    public async Task AccessLog_OnError_AlwaysLogsErrorMessage()
    {
        var writer = new CapturingAccessLogWriter();
        using var host = await BuildHostAsync(writer, logResponseBodyOnSuccess: false);

        var response = await host.GetTestClient().GetAsync("/api/test/error");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        var entry = Assert.Single(writer.Entries);
        Assert.False(entry.Success);
        Assert.Equal(500, entry.StatusCode);
        Assert.NotNull(entry.ErrorMessage);
        Assert.NotNull(entry.Response);
        Assert.Equal(500, ReadResponseCode(entry.Response));
    }

    [Fact]
    public async Task AccessLog_CreatedResult_RecordsFinalStatusCode()
    {
        var writer = new CapturingAccessLogWriter();
        using var host = await BuildHostAsync(writer, logResponseBodyOnSuccess: false);

        var response = await host.GetTestClient().GetAsync("/api/test/created");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var entry = Assert.Single(writer.Entries);
        Assert.Equal(201, entry.StatusCode);
        Assert.True(entry.Success);
        Assert.Null(entry.Response);
    }

    // ===================================================================
    //  ApiResponse —— 200 自动包装
    // ===================================================================

    [Fact]
    public async Task ApiResponse_WrapsNormalResult()
    {
        var writer = new CapturingAccessLogWriter();
        using var host = await BuildHostAsync(writer, logResponseBodyOnSuccess: false);

        var response = await host.GetTestClient().GetAsync("/api/test/hello");
        var body = await response.Content.ReadAsStringAsync();
        var root = JsonNode.Parse(body)!;

        Assert.Equal(200, (int)root["code"]!);
        Assert.Equal("success", (string)root["message"]!);
        Assert.True((bool)root["success"]!);
    }

    // ===================================================================
    //  ApiResponse —— 异常 → 500 Fail
    // ===================================================================

    [Fact]
    public async Task ApiResponse_ExceptionMiddleware_ReturnsFail()
    {
        var writer = new CapturingAccessLogWriter();
        using var host = await BuildHostAsync(writer, logResponseBodyOnSuccess: false);

        var response = await host.GetTestClient().GetAsync("/api/test/error");
        var body = await response.Content.ReadAsStringAsync();
        var root = JsonNode.Parse(body)!;

        Assert.Equal(500, (int)root["code"]!);
        Assert.False((bool)root["success"]!);
    }

    // ===================================================================
    //  ApiResponse —— 404 也包装
    // ===================================================================

    [Fact]
    public async Task ApiResponse_NotFound_ReturnsWrapped404()
    {
        var writer = new CapturingAccessLogWriter();
        using var host = await BuildHostAsync(writer, logResponseBodyOnSuccess: false);

        var response = await host.GetTestClient().GetAsync("/api/nonexistent");
        var body = await response.Content.ReadAsStringAsync();
        var root = JsonNode.Parse(body)!;

        Assert.Equal(404, (int)root["code"]!);
        Assert.False((bool)root["success"]!);

        var entry = Assert.Single(writer.Entries);
        Assert.Equal(404, entry.StatusCode);
        Assert.False(entry.Success);
        Assert.NotNull(entry.Response);
        Assert.Equal(404, ReadResponseCode(entry.Response));
    }

    // ===================================================================
    //  Helpers
    // ===================================================================

    private static async Task<IHost> BuildHostAsync(
        CapturingAccessLogWriter writer,
        bool logResponseBodyOnSuccess)
    {
        var builder = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddSingleton<IAccessLogWriter>(writer);
                    services.AddControllers();
                    services.AddRaindropAspNetCore(logResponseBodyOnSuccess);
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseRaindropAspNetCore();
                    app.UseEndpoints(endpoints => endpoints.MapControllers());
                });
            });

        var host = await builder.StartAsync();
        return host;
    }

    private static int? ReadResponseCode(object? response)
    {
        return response?.GetType().GetProperty("Code")?.GetValue(response) as int?;
    }

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
