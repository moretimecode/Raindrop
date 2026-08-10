namespace Raindrop.AspNetCore.Filters;

/// <summary>
/// AccessLogFilter 的配置，通过 DI 注入。
/// </summary>
public sealed record AccessLogFilterConfig(bool LogResponseBodyOnSuccess);
