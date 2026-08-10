using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Microsoft.Extensions.Configuration;

/// <summary>
/// EF Core 配置源的 <see cref="IConfigurationBuilder"/> 扩展方法。
/// </summary>
public static class EFConfigurationExtensions
{
    /// <summary>
    /// 添加 EF Core 数据库作为配置源，使用简化重载。
    /// </summary>
    public static IConfigurationBuilder AddEfConfiguration<TDbContext, TEntity, TConfig>(
        this IConfigurationBuilder builder,
        Func<TDbContext> dbContextFactory,
        Expression<Func<TDbContext, IQueryable<TEntity>>> queryExpression,
        Func<List<TEntity>, TConfig> mapper,
        string? sectionName = null,
        TimeSpan? refreshInterval = null)
        where TDbContext : DbContext
        where TEntity : class
        where TConfig : class
    {
        var options = new EFConfigurationOptions<TDbContext, TEntity, TConfig>
        {
            DbContextFactory = dbContextFactory,
            Query = queryExpression,
            Mapper = mapper,
            SectionName = sectionName ?? typeof(TConfig).Name,
            RefreshInterval = refreshInterval ?? TimeSpan.FromSeconds(5),
        };

        return builder.Add(new EFConfigurationSource<TDbContext, TEntity, TConfig>(options));
    }

    /// <summary>
    /// 添加 EF Core 数据库作为配置源，使用 Fluent Builder 重载。
    /// 支持委托映射和 Mapster 映射两种方式。
    /// </summary>
    public static IConfigurationBuilder AddEfConfiguration<TDbContext, TEntity, TConfig>(
        this IConfigurationBuilder builder,
        Action<EFConfigurationOptions<TDbContext, TEntity, TConfig>> configure)
        where TDbContext : DbContext
        where TEntity : class
        where TConfig : class
    {
        var options = new EFConfigurationOptions<TDbContext, TEntity, TConfig>();
        configure(options);

        if (options.DbContextFactory is null)
            throw new InvalidOperationException("DbContextFactory 是必需的。");
        if (options.Query is null)
            throw new InvalidOperationException("Query 是必需的。");
        if (options.Mapper is null && options.MapsterConfig is null)
            throw new InvalidOperationException("必须提供 Mapper 委托或 MapsterConfig。");

        return builder.Add(new EFConfigurationSource<TDbContext, TEntity, TConfig>(options));
    }
}
