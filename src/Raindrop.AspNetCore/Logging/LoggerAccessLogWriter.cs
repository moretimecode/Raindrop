using Microsoft.Extensions.Logging;
using Raindrop.Observability.Abstractions;

namespace Raindrop.AspNetCore.Logging;

/// <summary>
/// 基于 ILogger 的访问日志写入器。
/// 应用可以通过日志 category 将访问日志分流到独立文件。
/// </summary>
public sealed class LoggerAccessLogWriter(ILoggerFactory loggerFactory) : IAccessLogWriter
{
    public const string CategoryName = "Raindrop.AccessLog";

    private readonly ILogger _logger = loggerFactory.CreateLogger(CategoryName);

    public ValueTask WriteAsync(AccessLogEntry entry, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "访问日志 {@AccessLogEntry}",
            entry);
        return ValueTask.CompletedTask;
    }
}
