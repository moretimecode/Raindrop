using Microsoft.Extensions.Configuration;

namespace Raindrop.AppConfiguration;

public static class ConfigurationExtensions
{
    /// <summary>
    /// 从 <see cref="IConfiguration"/> 中获取指定 key 的强类型配置节，
    /// 配置节不存在或无法反序列化时将抛出异常。
    /// </summary>
    public static T GetRequired<T>(this IConfiguration configuration, string key)
        where T : class
    {
        return configuration.GetRequiredSection(key).Get<T>()
            ?? throw new InvalidOperationException(
                $"配置节 '{key}' 已找到，但无法反序列化为类型 '{typeof(T).Name}'。");
    }
}
