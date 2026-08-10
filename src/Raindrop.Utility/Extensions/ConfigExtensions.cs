using Microsoft.Extensions.Configuration;

namespace Raindrop.Utility.Extensions;

public static class ConfigExtensions
{
    /// <summary>
    /// 读取配置值，Key 不存在或转换失败时返回默认值。
    /// </summary>
    public static T? GetValue<T>(this IConfiguration config, string key, T? defaultValue = default)
        where T : IConvertible
    {
        var value = config.GetSection(key).Value;
        if (value == null)
            return defaultValue;

        return ConvertExtensions.To<T>(value, defaultValue);
    }
}
