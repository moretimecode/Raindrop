using Com.Ctrip.Framework.Apollo;
using Com.Ctrip.Framework.Apollo.Enums;
using Microsoft.Extensions.Configuration;

namespace Raindrop.AppConfiguration.Apollo;

public static class ApolloConfigurationBuilderExtensions
{
    /// <summary>
    /// 将 Apollo 作为远程配置源添加到 <see cref="IConfigurationBuilder"/> 中。
    /// 从已加载的配置的 "apollo" 配置节读取 Apollo 连接信息（MetaServer、AppId 等），
    /// 该配置节必须已通过其他配置源（如 <see cref="AppConfiguration.ConfigurationBuilderExtensions.AddLocalJson"/>）加载。
    /// </summary>
    public static IConfigurationBuilder AddApollo(
        this IConfigurationBuilder builder)
    {
        return AddApollo(builder, "apollo");
    }

    /// <summary>
    /// 将 Apollo 作为远程配置源添加到 <see cref="IConfigurationBuilder"/> 中，
    /// 使用指定的配置节 key 定位 Apollo 连接信息。
    /// </summary>
    public static IConfigurationBuilder AddApollo(
        this IConfigurationBuilder builder,
        string apolloSectionKey)
    {
        var config = builder.Build();
        var apolloOptions = config.GetSection(apolloSectionKey).Get<ApolloOptions>();

        if (apolloOptions is null)
        {
            throw new InvalidOperationException(
                $"Apollo 配置节 '{apolloSectionKey}' 缺失或无效，" +
                "请确保配置中包含有效的 Apollo 配置节，且已配置 MetaServer。");
        }

        Console.WriteLine($"Apollo MetaServer: {apolloOptions.MetaServer}");

        var apolloBuilder = builder.AddApollo(apolloOptions);

        var namespaceConfig = config.GetValue<string>($"{apolloSectionKey}:PublicNamespaces");
        if (!string.IsNullOrWhiteSpace(namespaceConfig))
        {
            foreach (var ns in namespaceConfig.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = ns.Trim();
                if (trimmed.Length == 0) continue;

                apolloBuilder.AddNamespace(trimmed, GetConfigFileFormat(trimmed));
            }
        }

        apolloBuilder.AddDefault();
        return builder;
    }

    private static ConfigFileFormat GetConfigFileFormat(string ns)
    {
        var lastDot = ns.LastIndexOf('.');
        if (lastDot < 0 || lastDot >= ns.Length - 1)
            return ConfigFileFormat.Properties;

        return ns[(lastDot + 1)..] switch
        {
            "properties" => ConfigFileFormat.Properties,
            "xml" => ConfigFileFormat.Xml,
            "json" => ConfigFileFormat.Json,
            "yml" => ConfigFileFormat.Yml,
            "yaml" => ConfigFileFormat.Yaml,
            "txt" => ConfigFileFormat.Txt,
            _ => ConfigFileFormat.Properties,
        };
    }
}
