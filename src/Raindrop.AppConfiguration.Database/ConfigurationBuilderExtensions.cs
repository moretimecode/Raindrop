using System.Data;
using Raindrop.AppConfiguration.Database;

namespace Microsoft.Extensions.Configuration;

/// <summary>
/// 数据库配置源的 <see cref="IConfigurationBuilder"/> 扩展方法。
/// </summary>
public static class DatabaseConfigurationBuilderExtensions
{
    /// <summary>
    /// 添加数据库作为配置源。
    /// </summary>
    public static IConfigurationBuilder AddDbConfiguration(
        this IConfigurationBuilder builder,
        DatabaseConfigOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return builder.Add(new DatabaseConfigurationSource(options));
    }

    /// <summary>
    /// 添加数据库作为配置源。
    /// </summary>
    /// <param name="builder"><see cref="IConfigurationBuilder"/></param>
    /// <param name="buildConnection">创建数据库连接的工厂委托。</param>
    /// <param name="sql">查询配置的 SQL 语句。</param>
    /// <param name="sectionName">配置节点名称，作为 key 前缀。默认 "DbConfig"。</param>
    /// <param name="reloadOnChange">是否启用热重载。默认 true。</param>
    /// <param name="refreshInterval">刷新间隔，单位秒。默认 5 秒。</param>
    /// <param name="usePascalNaming">是否将列名转换为 PascalCase。默认 true。</param>
    public static IConfigurationBuilder AddDbConfiguration(
        this IConfigurationBuilder builder,
        Func<IDbConnection> buildConnection,
        string sql,
        string sectionName = "DbConfig",
        bool reloadOnChange = true,
        int refreshInterval = 5,
        bool usePascalNaming = true)
    {
        ArgumentNullException.ThrowIfNull(buildConnection);
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);

        var options = new DatabaseConfigOptions
        {
            BuildConnection = buildConnection,
            Sql = sql,
            SectionName = sectionName,
            ReloadOnChange = reloadOnChange,
            RefreshInterval = TimeSpan.FromSeconds(refreshInterval),
            UsePascalNaming = usePascalNaming,
        };

        return builder.Add(new DatabaseConfigurationSource(options));
    }
}
