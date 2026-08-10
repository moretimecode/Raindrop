namespace Raindrop.AspNetCore.Filters;

/// <summary>
/// 控制访问日志的记录粒度。
/// 打在 Controller 或 Action 上可覆盖全局默认设置。
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public sealed class AccessLogAttribute : Attribute
{
    /// <summary>
    /// 是否记录完整响应体。默认 false（不记录 200 响应体）。
    /// </summary>
    public bool Full { get; set; }
}
