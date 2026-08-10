using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Raindrop.AspNetCore.Middlewares;

/// <summary>
/// 响应头快照。
/// 用于重写统一响应体时保留上游中间件已经设置的业务头、安全头、CORS 头等。
/// </summary>
internal sealed class ResponseHeaderSnapshot
{
    private static readonly HashSet<string> ExcludedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Content-Length",
        "Content-Type"
    };

    private readonly Dictionary<string, StringValues> _headers;

    private ResponseHeaderSnapshot(Dictionary<string, StringValues> headers)
    {
        _headers = headers;
    }

    public static ResponseHeaderSnapshot Capture(HttpResponse response)
    {
        var headers = response.Headers
            .Where(header => !ExcludedHeaders.Contains(header.Key))
            .ToDictionary(header => header.Key, header => header.Value, StringComparer.OrdinalIgnoreCase);

        return new ResponseHeaderSnapshot(headers);
    }

    public void Restore(HttpResponse response)
    {
        foreach (var (key, value) in _headers)
        {
            response.Headers[key] = value;
        }
    }
}
