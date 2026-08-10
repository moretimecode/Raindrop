namespace Raindrop.Observability.Abstractions;

/// <summary>
/// 访问日志写入器。
/// 具体日志框架通过该接口适配，框架本身不绑定 NLog、Serilog 等实现。
/// </summary>
public interface IAccessLogWriter
{
    ValueTask WriteAsync(AccessLogEntry entry, CancellationToken cancellationToken = default);
}
