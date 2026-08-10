using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Raindrop.AppConfiguration;

namespace Raindrop.AppConfiguration.Tests;

[Collection(PostgreSqlCollection.Name)]
public class EfConfigurationProviderIntegrationTests
{
    private readonly PostgreSqlTestFixture _postgres;

    public EfConfigurationProviderIntegrationTests(PostgreSqlTestFixture postgres)
    {
        _postgres = postgres;
    }

    [Fact]
    public async Task AddEfConfiguration_LoadsAndMapsRowsFromPostgreSql()
    {
        await _postgres.ResetEfConfigurationRowsAsync();

        var configuration = new ConfigurationBuilder()
            .AddEfConfiguration<PostgreSqlConfigurationDbContext, EfConfigurationEntity, EfConfigurationOptions>(
                CreateDbContext,
                db => db.Configurations.AsNoTracking().OrderBy(row => row.Id),
                rows =>
                {
                    var row = rows.Single();

                    return new EfConfigurationOptions
                    {
                        AppName = row.AppName,
                        MaxItems = row.MaxItems,
                        FeatureFlags = new EfFeatureFlagsOptions
                        {
                            EnableNewUI = row.EnableNewUI
                        }
                    };
                },
                sectionName: "EfConfig",
                refreshInterval: TimeSpan.FromMinutes(5))
            .Build();

        var options = configuration.GetSection("EfConfig").Get<EfConfigurationOptions>();

        Assert.NotNull(options);
        Assert.Equal("Raindrop.EfCore", options.AppName);
        Assert.Equal(50, options.MaxItems);
        Assert.True(options.FeatureFlags.EnableNewUI);
    }

    [Fact]
    public void AddEfConfiguration_InitialLoadFailure_ThrowsByDefault()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ConfigurationBuilder()
                .AddEfConfiguration<PostgreSqlConfigurationDbContext, EfConfigurationEntity, EfConfigurationOptions>(
                    () => throw new InvalidOperationException("context unavailable"),
                    db => db.Configurations,
                    rows => new EfConfigurationOptions())
                .Build());

        Assert.Contains("EF 配置加载失败", exception.Message);
        Assert.IsType<InvalidOperationException>(exception.InnerException);
    }

    // ===================================================================
    //  热重载：数据库变更后自动刷新配置
    // ===================================================================

    [Fact]
    public async Task HotReload_DataChangedInDatabase_ConfigRefreshes()
    {
        await _postgres.ResetEfConfigurationRowsAsync();

        var configuration = new ConfigurationBuilder()
            .AddEfConfiguration<PostgreSqlConfigurationDbContext, EfConfigurationEntity, EfConfigurationOptions>(
                CreateDbContext,
                db => db.Configurations.AsNoTracking().OrderBy(row => row.Id),
                rows =>
                {
                    var row = rows.Single();
                    return new EfConfigurationOptions
                    {
                        AppName = row.AppName,
                        MaxItems = row.MaxItems,
                        FeatureFlags = new EfFeatureFlagsOptions { EnableNewUI = row.EnableNewUI }
                    };
                },
                sectionName: "HotReload",
                refreshInterval: TimeSpan.FromSeconds(1))
            .Build();

        // 初始值
        var initial = configuration.GetSection("HotReload").Get<EfConfigurationOptions>()!;
        Assert.Equal(50, initial.MaxItems);

        // 直接更新数据库
        await UpdateEfConfigMaxItemsAsync(100);

        // 等待热重载生效（刷新间隔 1s + 缓冲）
        var reloaded = await WaitForConfigChangeAsync(
            configuration,
            "HotReload",
            expectedMaxItems: 100,
            timeoutSeconds: 10);

        Assert.NotNull(reloaded);
        Assert.Equal(100, reloaded.MaxItems);
        Assert.Equal("Raindrop.EfCore", reloaded.AppName);
    }

    // ===================================================================
    //  KeepLastKnownGood：刷新失败时保留上一次的配置
    // ===================================================================

    [Fact]
    public async Task ReloadFailure_KeepLastKnownGood_PreservesPreviousConfig()
    {
        await _postgres.ResetEfConfigurationRowsAsync();

        // Load() 会被调用两次（Provider 构造函数 + ConfigurationRoot 构造函数），
        // 所以前两次成功，第三次（Timer 触发的热重载）才抛异常。
        var factoryCallCount = 0;

        PostgreSqlConfigurationDbContext CountingFactory()
        {
            var count = Interlocked.Increment(ref factoryCallCount);
            if (count > 2)
            {
                throw new InvalidOperationException("Simulated transient DB failure");
            }

            return CreateDbContext();
        }

        var configuration = new ConfigurationBuilder()
            .AddEfConfiguration<PostgreSqlConfigurationDbContext, EfConfigurationEntity, EfConfigurationOptions>(
                options =>
                {
                    options
                        .WithDbContextFactory(CountingFactory)
                        .WithQuery(db => db.Configurations.AsNoTracking().OrderBy(row => row.Id))
                        .WithMapper(rows =>
                        {
                            var row = rows.Single();
                            return new EfConfigurationOptions
                            {
                                AppName = row.AppName,
                                MaxItems = row.MaxItems,
                                FeatureFlags = new EfFeatureFlagsOptions { EnableNewUI = row.EnableNewUI }
                            };
                        })
                        .WithSection("KeepLastKnown")
                        .WithRefreshInterval(TimeSpan.FromSeconds(1))
                        .WithReloadFailureBehavior(ConfigurationLoadFailureBehavior.KeepLastKnownGood);
                })
            .Build();

        // 初始加载成功
        var initial = configuration.GetSection("KeepLastKnown").Get<EfConfigurationOptions>()!;
        Assert.Equal(50, initial.MaxItems);
        Assert.True(initial.FeatureFlags.EnableNewUI);

        // 等待至少一次刷新尝试（factory 第二次调用会抛异常）
        await Task.Delay(TimeSpan.FromSeconds(3));

        // 验证配置仍可用（KeepLastKnownGood 保留了上一次数据）
        var afterReload = configuration.GetSection("KeepLastKnown").Get<EfConfigurationOptions>()!;
        Assert.NotNull(afterReload);
        Assert.Equal(50, afterReload.MaxItems);
        Assert.Equal("Raindrop.EfCore", afterReload.AppName);
        Assert.True(afterReload.FeatureFlags.EnableNewUI);

        Assert.True(factoryCallCount >= 3, "至少触发一次刷新（factory 被调用 >= 3 次：构造 2 次 + 刷新 1 次）");
    }

    // ===================================================================
    //  Helpers
    // ===================================================================

    private async Task UpdateEfConfigMaxItemsAsync(int newMaxItems)
    {
        await using var connection = _postgres.CreateConnection();
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText =
            $"UPDATE {PostgreSqlTestFixture.SchemaName}.ef_configurations SET max_items = @p0";
        command.Parameters.AddWithValue("p0", newMaxItems);

        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// 轮询等待配置变更生效。在超时时间内持续检查目标 section 的值是否达到预期。
    /// </summary>
    private static async Task<EfConfigurationOptions?> WaitForConfigChangeAsync(
        IConfigurationRoot configuration,
        string sectionName,
        int expectedMaxItems,
        int timeoutSeconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);

        while (DateTime.UtcNow < deadline)
        {
            var options = configuration.GetSection(sectionName).Get<EfConfigurationOptions>();
            if (options?.MaxItems == expectedMaxItems)
            {
                return options;
            }

            await Task.Delay(500);
        }

        return configuration.GetSection(sectionName).Get<EfConfigurationOptions>();
    }

    private PostgreSqlConfigurationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<PostgreSqlConfigurationDbContext>()
            .UseNpgsql(_postgres.ConnectionString)
            .Options;

        return new PostgreSqlConfigurationDbContext(options);
    }

    public sealed class PostgreSqlConfigurationDbContext : DbContext
    {
        public PostgreSqlConfigurationDbContext(DbContextOptions<PostgreSqlConfigurationDbContext> options)
            : base(options)
        {
        }

        public DbSet<EfConfigurationEntity> Configurations => Set<EfConfigurationEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<EfConfigurationEntity>(entity =>
            {
                entity.ToTable("ef_configurations", PostgreSqlTestFixture.SchemaName);
                entity.HasKey(row => row.Id);
                entity.Property(row => row.Id).HasColumnName("id");
                entity.Property(row => row.AppName).HasColumnName("app_name");
                entity.Property(row => row.MaxItems).HasColumnName("max_items");
                entity.Property(row => row.EnableNewUI).HasColumnName("enable_new_ui");
            });
        }
    }

    public sealed class EfConfigurationEntity
    {
        public int Id { get; set; }

        public string AppName { get; set; } = string.Empty;

        public int MaxItems { get; set; }

        public bool EnableNewUI { get; set; }
    }

    private sealed class EfConfigurationOptions
    {
        public string AppName { get; set; } = string.Empty;

        public int MaxItems { get; set; }

        public EfFeatureFlagsOptions FeatureFlags { get; set; } = new();
    }

    private sealed class EfFeatureFlagsOptions
    {
        public bool EnableNewUI { get; set; }
    }
}
