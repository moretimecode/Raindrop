using System.Linq.Expressions;
using Mapster;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Raindrop.AppConfiguration;

namespace Raindrop.AppConfiguration.Tests;

/// <summary>
/// EFConfigurationOptions Fluent Builder API 的单元测试。
/// </summary>
public class EFConfigurationOptionsTests
{
    private sealed class FakeDbContext : DbContext
    {
        public FakeDbContext(DbContextOptions<FakeDbContext> options) : base(options) { }
        public DbSet<FakeEntity> Entities => Set<FakeEntity>();
    }

    private sealed class FakeEntity
    {
        public int Id { get; set; }
        public string Value { get; set; } = string.Empty;
    }

    private sealed class FakeConfig
    {
        public string Name { get; set; } = string.Empty;
    }

    // ===================================================================
    //  默认值
    // ===================================================================

    [Fact]
    public void Defaults_SectionName_UsesConfigTypeName()
    {
        var options = new EFConfigurationOptions<FakeDbContext, FakeEntity, FakeConfig>();

        Assert.Equal(nameof(FakeConfig), options.SectionName);
    }

    [Fact]
    public void Defaults_RefreshInterval_IsFiveSeconds()
    {
        var options = new EFConfigurationOptions<FakeDbContext, FakeEntity, FakeConfig>();

        Assert.Equal(TimeSpan.FromSeconds(5), options.RefreshInterval);
    }

    [Fact]
    public void Defaults_InitialLoadFailureBehavior_IsThrow()
    {
        var options = new EFConfigurationOptions<FakeDbContext, FakeEntity, FakeConfig>();

        Assert.Equal(ConfigurationLoadFailureBehavior.Throw, options.InitialLoadFailureBehavior);
    }

    [Fact]
    public void Defaults_ReloadFailureBehavior_IsKeepLastKnownGood()
    {
        var options = new EFConfigurationOptions<FakeDbContext, FakeEntity, FakeConfig>();

        Assert.Equal(ConfigurationLoadFailureBehavior.KeepLastKnownGood, options.ReloadFailureBehavior);
    }

    // ===================================================================
    //  WithDbContextFactory
    // ===================================================================

    [Fact]
    public void WithDbContextFactory_SetsFactory()
    {
        var factory = () => new FakeDbContext(
            new DbContextOptionsBuilder<FakeDbContext>().Options);

        var options = new EFConfigurationOptions<FakeDbContext, FakeEntity, FakeConfig>()
            .WithDbContextFactory(factory);

        Assert.Same(factory, options.DbContextFactory);
    }

    // ===================================================================
    //  WithQuery
    // ===================================================================

    [Fact]
    public void WithQuery_SetsQuery()
    {
        Expression<Func<FakeDbContext, IQueryable<FakeEntity>>> query =
            db => db.Entities;

        var options = new EFConfigurationOptions<FakeDbContext, FakeEntity, FakeConfig>()
            .WithQuery(query);

        Assert.Same(query, options.Query);
    }

    // ===================================================================
    //  WithMapper
    // ===================================================================

    [Fact]
    public void WithMapper_SetsMapperAndClearsMapster()
    {
        var mapper = (List<FakeEntity> rows) => new FakeConfig { Name = "mapped" };

        var options = new EFConfigurationOptions<FakeDbContext, FakeEntity, FakeConfig>()
            .WithMapster()          // 先设 Mapster
            .WithMapper(mapper);    // 再切到委托

        Assert.Same(mapper, options.Mapper);
        Assert.Null(options.MapsterConfig);
    }

    // ===================================================================
    //  WithMapster
    // ===================================================================

    [Fact]
    public void WithMapster_NoConfig_UsesGlobalSettings()
    {
        var options = new EFConfigurationOptions<FakeDbContext, FakeEntity, FakeConfig>()
            .WithMapster();

        Assert.Same(TypeAdapterConfig.GlobalSettings, options.MapsterConfig);
        Assert.Null(options.Mapper);
    }

    [Fact]
    public void WithMapster_CustomConfig_SetsConfigAndClearsMapper()
    {
        var config = new TypeAdapterConfig();

        var options = new EFConfigurationOptions<FakeDbContext, FakeEntity, FakeConfig>()
            .WithMapper(rows => new FakeConfig())   // 先设委托
            .WithMapster(config);                    // 再切到 Mapster

        Assert.Same(config, options.MapsterConfig);
        Assert.Null(options.Mapper);
    }

    // ===================================================================
    //  WithSection
    // ===================================================================

    [Fact]
    public void WithSection_SetsSectionName()
    {
        var options = new EFConfigurationOptions<FakeDbContext, FakeEntity, FakeConfig>()
            .WithSection("CustomSection");

        Assert.Equal("CustomSection", options.SectionName);
    }

    // ===================================================================
    //  WithRefreshInterval
    // ===================================================================

    [Fact]
    public void WithRefreshInterval_SetsInterval()
    {
        var interval = TimeSpan.FromMinutes(10);

        var options = new EFConfigurationOptions<FakeDbContext, FakeEntity, FakeConfig>()
            .WithRefreshInterval(interval);

        Assert.Equal(interval, options.RefreshInterval);
    }

    // ===================================================================
    //  WithInitialLoadFailureBehavior
    // ===================================================================

    [Fact]
    public void WithInitialLoadFailureBehavior_SetsBehavior()
    {
        var options = new EFConfigurationOptions<FakeDbContext, FakeEntity, FakeConfig>()
            .WithInitialLoadFailureBehavior(ConfigurationLoadFailureBehavior.KeepLastKnownGood);

        Assert.Equal(ConfigurationLoadFailureBehavior.KeepLastKnownGood,
            options.InitialLoadFailureBehavior);
    }

    // ===================================================================
    //  WithReloadFailureBehavior
    // ===================================================================

    [Fact]
    public void WithReloadFailureBehavior_SetsBehavior()
    {
        var options = new EFConfigurationOptions<FakeDbContext, FakeEntity, FakeConfig>()
            .WithReloadFailureBehavior(ConfigurationLoadFailureBehavior.Throw);

        Assert.Equal(ConfigurationLoadFailureBehavior.Throw,
            options.ReloadFailureBehavior);
    }

    // ===================================================================
    //  OnLoadFailed
    // ===================================================================

    [Fact]
    public void OnLoadFailed_SetsHandler()
    {
        var called = false;
        Action<Exception> handler = _ => called = true;

        var options = new EFConfigurationOptions<FakeDbContext, FakeEntity, FakeConfig>()
            .OnLoadFailed(handler);

        options.OnLoadException?.Invoke(new InvalidOperationException());
        Assert.True(called);
    }

    // ===================================================================
    //  WithLoggerFactory
    // ===================================================================

    [Fact]
    public void WithLoggerFactory_SetsLoggerFactory()
    {
        using var factory = LoggerFactory.Create(_ => { });

        var options = new EFConfigurationOptions<FakeDbContext, FakeEntity, FakeConfig>()
            .WithLoggerFactory(factory);

        Assert.Same(factory, options.LoggerFactory);
    }

    // ===================================================================
    //  WithChangeDetector
    // ===================================================================

    [Fact]
    public void WithChangeDetector_SetsDetector()
    {
        Func<List<FakeEntity>, List<FakeEntity>, bool> detector = (_, _) => true;

        var options = new EFConfigurationOptions<FakeDbContext, FakeEntity, FakeConfig>()
            .WithChangeDetector(detector);

        Assert.Same(detector, options.ChangeDetector);
    }

    // ===================================================================
    //  Fluent Chaining
    // ===================================================================

    [Fact]
    public void FluentApi_AllMethodsReturnSameInstance()
    {
        Expression<Func<FakeDbContext, IQueryable<FakeEntity>>> query = db => db.Entities;
        var factory = () => new FakeDbContext(new DbContextOptionsBuilder<FakeDbContext>().Options);
        var mapper = (List<FakeEntity> rows) => new FakeConfig();

        var options = new EFConfigurationOptions<FakeDbContext, FakeEntity, FakeConfig>();

        var result = options
            .WithDbContextFactory(factory)
            .WithQuery(query)
            .WithMapper(mapper)
            .WithSection("Test")
            .WithRefreshInterval(TimeSpan.FromSeconds(30))
            .WithInitialLoadFailureBehavior(ConfigurationLoadFailureBehavior.KeepLastKnownGood)
            .WithReloadFailureBehavior(ConfigurationLoadFailureBehavior.Throw);

        Assert.Same(options, result);
        Assert.Same(factory, options.DbContextFactory);
        Assert.Same(query, options.Query);
        Assert.Same(mapper, options.Mapper);
        Assert.Equal("Test", options.SectionName);
        Assert.Equal(TimeSpan.FromSeconds(30), options.RefreshInterval);
        Assert.Equal(ConfigurationLoadFailureBehavior.KeepLastKnownGood, options.InitialLoadFailureBehavior);
        Assert.Equal(ConfigurationLoadFailureBehavior.Throw, options.ReloadFailureBehavior);
    }
}
