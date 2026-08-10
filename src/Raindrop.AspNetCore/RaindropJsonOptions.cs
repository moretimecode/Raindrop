using System.Text.Json;

namespace Raindrop.AspNetCore;

/// <summary>
/// Raindrop HTTP 响应默认 JSON 配置。
/// </summary>
public static class RaindropJsonOptions
{
    public static JsonSerializerOptions Default { get; } = new(JsonSerializerDefaults.Web);
}
