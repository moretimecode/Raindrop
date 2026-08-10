using Microsoft.Extensions.Configuration;

namespace Raindrop.AppConfiguration;

public static class ConfigurationBuilderExtensions
{
    private const string DefaultConfigDirectory = "Configs";

    /// <summary>
    /// 加载基础配置栈，包含：
    ///   1. appsettings.json
    ///   2. appsettings.{environment}.json
    ///   3. 环境变量
    /// 默认从相对于 AppContext.BaseDirectory 的 "Configs" 目录加载。
    /// </summary>
    public static IConfigurationBuilder AddLocalJson(
        this IConfigurationBuilder builder,
        string environment)
    {
        return AddLocalJson(builder, environment, DefaultConfigDirectory);
    }

    /// <summary>
    /// 从指定目录加载基础配置栈，包含：
    ///   1. appsettings.json
    ///   2. appsettings.{environment}.json
    ///   3. 环境变量
    /// </summary>
    public static IConfigurationBuilder AddLocalJson(
        this IConfigurationBuilder builder,
        string environment,
        string configDirectory)
    {
        var configPath = Path.Combine(AppContext.BaseDirectory, configDirectory);

        return builder
            .SetBasePath(configPath)
            .AddJsonFile("appsettings.json", false, true)
            .AddJsonFile($"appsettings.{environment}.json", false, true)
            .AddEnvironmentVariables();
    }
}
