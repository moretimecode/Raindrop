using Microsoft.AspNetCore.Http;

namespace Raindrop.AspNetCore.Logging;

/// <summary>
/// 一次 HTTP 请求内的访问日志采集上下文。
/// Filter 和 Middleware 通过 HttpContext.Features 共享，不暴露给业务代码。
/// </summary>
internal sealed class AccessLogFeature
{
    public object? Request { get; set; }

    public object? Response { get; set; }

    public bool HasResponse { get; set; }

    public string? ErrorMessage { get; set; }
}

internal static class AccessLogFeatureExtensions
{
    public static AccessLogFeature GetOrCreateAccessLogFeature(this HttpContext context)
    {
        var feature = context.Features.Get<AccessLogFeature>();
        if (feature is not null)
            return feature;

        feature = new AccessLogFeature();
        context.Features.Set(feature);
        return feature;
    }
}
