using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Raindrop.AppConfiguration;

namespace Raindrop.AppConfiguration.Tests;

/// <summary>
/// EFConfigurationExtensions Fluent Builder 重载和参数校验的单元测试。
/// </summary>
public class EFConfigurationExtensionsTests
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

    private static FakeDbContext CreateDbContext()
    {
        return new FakeDbContext(new DbContextOptionsBuilder<FakeDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    }

    private static Expression<Func<FakeDbContext, IQueryable<FakeEntity>>> DefaultQuery =>
        db => db.Entities;

    private static Func<List<FakeEntity>, FakeConfig> DefaultMapper =>
        rows => new FakeConfig { Name = rows.FirstOrDefault()?.Value ?? "none" };

    // ===================================================================
    //  简化重载
    // ===================================================================

    [Fact]
    public void AddEfConfiguration_SimplifiedOverload_AddsSource()
    {
        var config = new ConfigurationBuilder()
            .AddEfConfiguration<FakeDbContext, FakeEntity, FakeConfig>(
                CreateDbContext,
                DefaultQuery,
                DefaultMapper,
                sectionName: "Simplified",
                refreshInterval: TimeSpan.FromMinutes(30))
            .Build();

        var section = config.GetSection("Simplified");
        Assert.NotNull(section);
    }

    [Fact]
    public void AddEfConfiguration_SimplifiedOverload_DefaultSectionName()
    {
        var config = new ConfigurationBuilder()
            .AddEfConfiguration<FakeDbContext, FakeEntity, FakeConfig>(
                CreateDbContext,
                DefaultQuery,
                DefaultMapper)
            .Build();

        // 默认 SectionName = typeof(FakeConfig).Name
        var section = config.GetSection(nameof(FakeConfig));
        Assert.NotNull(section);
    }

    // ===================================================================
    //  Fluent Builder 重载
    // ===================================================================

    [Fact]
    public void AddEfConfiguration_FluentOverload_AddsSource()
    {
        var config = new ConfigurationBuilder()
            .AddEfConfiguration<FakeDbContext, FakeEntity, FakeConfig>(options =>
            {
                options
                    .WithDbContextFactory(CreateDbContext)
                    .WithQuery(DefaultQuery)
                    .WithMapper(DefaultMapper)
                    .WithSection("Fluent");
            })
            .Build();

        var section = config.GetSection("Fluent");
        Assert.NotNull(section);
    }

    [Fact]
    public void AddEfConfiguration_FluentOverload_WithMapster()
    {
        var config = new ConfigurationBuilder()
            .AddEfConfiguration<FakeDbContext, FakeEntity, FakeConfig>(options =>
            {
                options
                    .WithDbContextFactory(CreateDbContext)
                    .WithQuery(DefaultQuery)
                    .WithMapster()
                    .WithSection("MapsterSection");
            })
            .Build();

        var section = config.GetSection("MapsterSection");
        Assert.NotNull(section);
    }

    [Fact]
    public void AddEfConfiguration_FluentOverload_WithCustomChangeDetector()
    {
        var config = new ConfigurationBuilder()
            .AddEfConfiguration<FakeDbContext, FakeEntity, FakeConfig>(options =>
            {
                options
                    .WithDbContextFactory(CreateDbContext)
                    .WithQuery(DefaultQuery)
                    .WithMapper(DefaultMapper)
                    .WithChangeDetector((old, @new) => old.Count != @new.Count);
            })
            .Build();

        Assert.NotNull(config);
    }

    // ===================================================================
    //  参数校验
    // ===================================================================

    [Fact]
    public void AddEfConfiguration_FluentOverload_ThrowsWhenNoDbContextFactory()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new ConfigurationBuilder()
                .AddEfConfiguration<FakeDbContext, FakeEntity, FakeConfig>(options =>
                {
                    options
                        .WithQuery(DefaultQuery)
                        .WithMapper(DefaultMapper);
                })
                .Build());

        Assert.Contains("DbContextFactory", ex.Message);
    }

    [Fact]
    public void AddEfConfiguration_FluentOverload_ThrowsWhenNoQuery()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new ConfigurationBuilder()
                .AddEfConfiguration<FakeDbContext, FakeEntity, FakeConfig>(options =>
                {
                    options
                        .WithDbContextFactory(CreateDbContext)
                        .WithMapper(DefaultMapper);
                })
                .Build());

        Assert.Contains("Query", ex.Message);
    }

    [Fact]
    public void AddEfConfiguration_FluentOverload_ThrowsWhenNoMapperOrMapster()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new ConfigurationBuilder()
                .AddEfConfiguration<FakeDbContext, FakeEntity, FakeConfig>(options =>
                {
                    options
                        .WithDbContextFactory(CreateDbContext)
                        .WithQuery(DefaultQuery);
                    // 没有 WithMapper 也没有 WithMapster
                })
                .Build());

        Assert.Contains("Mapper", ex.Message);
        Assert.Contains("MapsterConfig", ex.Message);
    }

    [Fact]
    public void AddEfConfiguration_FluentOverload_WithAllSettings()
    {
        using var loggerFactory = LoggerFactory.Create(_ => { });

        var config = new ConfigurationBuilder()
            .AddEfConfiguration<FakeDbContext, FakeEntity, FakeConfig>(options =>
            {
                options
                    .WithDbContextFactory(CreateDbContext)
                    .WithQuery(DefaultQuery)
                    .WithMapper(DefaultMapper)
                    .WithSection("FullConfig")
                    .WithRefreshInterval(TimeSpan.FromHours(1))
                    .WithInitialLoadFailureBehavior(ConfigurationLoadFailureBehavior.KeepLastKnownGood)
                    .WithReloadFailureBehavior(ConfigurationLoadFailureBehavior.Throw)
                    .WithLoggerFactory(loggerFactory)
                    .WithChangeDetector((old, @new) => false);
            })
            .Build();

        Assert.NotNull(config.GetSection("FullConfig"));
    }
}
