namespace Microsoft.Extensions.Configuration;

/// <summary>
/// 将配置对象生成 key-value 字典的接口。
/// </summary>
public interface IConfigurationKeyGenerator
{
    /// <summary>
    /// 将配置对象递归展平为 key-value 字典。
    /// </summary>
    Dictionary<string, string?> GenerateKeys<T>(T configObject) where T : class;
}
