using System.Linq.Expressions;
using Mapster;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Raindrop.AppConfiguration;

namespace Microsoft.Extensions.Configuration;

/// <summary>
/// EF Core 配置源的选项，使用 Fluent Builder 模式。
/// </summary>
public class EFConfigurationOptions<TDbContext, TEntity, TConfig>
    where TDbContext : DbContext
    where TEntity : class
    where TConfig : class
{
    /// <summary>
    /// 查询表达式，用于从 DbContext 中获取配置实体。
    /// </summary>
    public Expression<Func<TDbContext, IQueryable<TEntity>>>? Query { get; set; }

    /// <summary>
    /// 委托映射器。与 Mapster 互斥。
    /// </summary>
    public Func<List<TEntity>, TConfig>? Mapper { get; set; }

    /// <summary>
    /// DbContext 工厂委托，必填。
    /// </summary>
    public Func<TDbContext>? DbContextFactory { get; set; }

    /// <summary>
    /// 配置节点名称，默认使用 TConfig 的类型名。
    /// </summary>
    public string SectionName { get; set; } = typeof(TConfig).Name;

    /// <summary>
    /// 刷新间隔。默认 5 秒。
    /// </summary>
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromSeconds(5);

    public ConfigurationLoadFailureBehavior InitialLoadFailureBehavior { get; set; } =
        ConfigurationLoadFailureBehavior.Throw;

    public ConfigurationLoadFailureBehavior ReloadFailureBehavior { get; set; } =
        ConfigurationLoadFailureBehavior.KeepLastKnownGood;

    public Action<Exception>? OnLoadException { get; set; }

    /// <summary>
    /// 可选的日志工厂。为 null 时不输出日志。
    /// </summary>
    public ILoggerFactory? LoggerFactory { get; set; }

    /// <summary>
    /// 自定义变更检测器。为 null 时使用默认的反射比较。
    /// </summary>
    public Func<List<TEntity>, List<TEntity>, bool>? ChangeDetector { get; set; }

    /// <summary>
    /// Mapster 配置实例。设置后将使用 Mapster 进行映射。
    /// </summary>
    public TypeAdapterConfig? MapsterConfig { get; set; }

    public EFConfigurationOptions<TDbContext, TEntity, TConfig> WithDbContextFactory(
        Func<TDbContext> dbContextFactory)
    {
        DbContextFactory = dbContextFactory;
        return this;
    }

    public EFConfigurationOptions<TDbContext, TEntity, TConfig> WithQuery(
        Expression<Func<TDbContext, IQueryable<TEntity>>> query)
    {
        Query = query;
        return this;
    }

    public EFConfigurationOptions<TDbContext, TEntity, TConfig> WithMapper(
        Func<List<TEntity>, TConfig> mapper)
    {
        Mapper = mapper;
        MapsterConfig = null;
        return this;
    }

    public EFConfigurationOptions<TDbContext, TEntity, TConfig> WithMapster(
        TypeAdapterConfig? config = null)
    {
        MapsterConfig = config ?? TypeAdapterConfig.GlobalSettings;
        Mapper = null;
        return this;
    }

    public EFConfigurationOptions<TDbContext, TEntity, TConfig> WithSection(string sectionName)
    {
        SectionName = sectionName;
        return this;
    }

    public EFConfigurationOptions<TDbContext, TEntity, TConfig> WithRefreshInterval(TimeSpan interval)
    {
        RefreshInterval = interval;
        return this;
    }

    public EFConfigurationOptions<TDbContext, TEntity, TConfig> WithInitialLoadFailureBehavior(
        ConfigurationLoadFailureBehavior behavior)
    {
        InitialLoadFailureBehavior = behavior;
        return this;
    }

    public EFConfigurationOptions<TDbContext, TEntity, TConfig> WithReloadFailureBehavior(
        ConfigurationLoadFailureBehavior behavior)
    {
        ReloadFailureBehavior = behavior;
        return this;
    }

    public EFConfigurationOptions<TDbContext, TEntity, TConfig> OnLoadFailed(Action<Exception> handler)
    {
        OnLoadException = handler;
        return this;
    }

    public EFConfigurationOptions<TDbContext, TEntity, TConfig> WithLoggerFactory(
        ILoggerFactory loggerFactory)
    {
        LoggerFactory = loggerFactory;
        return this;
    }

    public EFConfigurationOptions<TDbContext, TEntity, TConfig> WithChangeDetector(
        Func<List<TEntity>, List<TEntity>, bool> detector)
    {
        ChangeDetector = detector;
        return this;
    }
}
