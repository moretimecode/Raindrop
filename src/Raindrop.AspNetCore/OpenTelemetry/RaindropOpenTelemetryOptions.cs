namespace Raindrop.AspNetCore.OpenTelemetry;

/// <summary>
/// Raindrop OpenTelemetry 内部配置。
/// </summary>
internal sealed class RaindropOpenTelemetryOptions
{
    /// <summary>
    /// 配置节点路径。
    /// </summary>
    public const string SectionName = "Raindrop:OpenTelemetry";

    /// <summary>
    /// 服务名称，缺失时不启用 OpenTelemetry。
    /// </summary>
    public string? ServiceName { get; set; }

    /// <summary>
    /// 服务命名空间。
    /// </summary>
    public string? ServiceNamespace { get; set; }

    /// <summary>
    /// 服务版本。
    /// </summary>
    public string? ServiceVersion { get; set; }

    /// <summary>
    /// 服务实例标识。
    /// </summary>
    public string? ServiceInstanceId { get; set; }

    /// <summary>
    /// 是否在未配置实例标识时自动生成。
    /// </summary>
    public bool AutoGenerateServiceInstanceId { get; set; }

    /// <summary>
    /// OTLP 导出端点，缺失时不启用 OpenTelemetry。
    /// </summary>
    public string? OtlpEndpoint { get; set; }

    /// <summary>
    /// 是否具备启动 OpenTelemetry SDK 的必要配置。
    /// </summary>
    public bool HasRequiredConfiguration()
    {
        return !string.IsNullOrWhiteSpace(ServiceName)
               && Uri.TryCreate(OtlpEndpoint, UriKind.Absolute, out _);
    }

    /// <summary>
    /// 获取有效的 OTLP 端点。
    /// </summary>
    public Uri GetOtlpEndpoint()
    {
        return new Uri(OtlpEndpoint!, UriKind.Absolute);
    }

    /// <summary>
    /// 解析最终使用的服务实例标识。
    /// </summary>
    public string? ResolveServiceInstanceId()
    {
        if (!string.IsNullOrWhiteSpace(ServiceInstanceId))
        {
            return ServiceInstanceId;
        }

        return AutoGenerateServiceInstanceId ? Guid.NewGuid().ToString("N") : null;
    }
}
