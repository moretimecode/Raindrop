namespace Raindrop.Observability.Abstractions;

/// <summary>
/// 访问日志条目。
/// 用于记录 HTTP、消息消费者等入口的一次完整调用事实。
/// </summary>
public sealed record AccessLogEntry(
    string TraceId,
    string Source,
    string Operation,
    string? Method,
    string? Path,
    object? Request,
    object? Response,
    int? StatusCode,
    long ElapsedMilliseconds,
    bool Success,
    string? ErrorMessage);
