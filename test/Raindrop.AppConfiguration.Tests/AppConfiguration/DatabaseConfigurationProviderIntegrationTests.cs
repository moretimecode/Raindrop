using Microsoft.Extensions.Configuration;
using Raindrop.AppConfiguration.Database;

namespace Raindrop.AppConfiguration.Tests;

[Collection(PostgreSqlCollection.Name)]
public class DatabaseConfigurationProviderIntegrationTests
{
    private readonly PostgreSqlTestFixture _postgres;

    public DatabaseConfigurationProviderIntegrationTests(PostgreSqlTestFixture postgres)
    {
        _postgres = postgres;
    }

    [Fact]
    public async Task AddDbConfiguration_LoadsRowsFromPostgreSql()
    {
        await _postgres.ResetDatabaseConfigurationRowsAsync();

        var configuration = new ConfigurationBuilder()
            .AddDbConfiguration(new DatabaseConfigOptions
            {
                BuildConnection = _postgres.CreateConnection,
                Sql = $"""
                    select app_name, max_items, feature_flags
                    from {PostgreSqlTestFixture.SchemaName}.database_configurations
                    order by id
                    """,
                SectionName = "DatabaseConfigs",
                ReloadOnChange = false
            })
            .Build();

        var rows = configuration.GetSection("DatabaseConfigs").Get<List<DatabaseConfigurationRow>>();

        Assert.NotNull(rows);
        var row = Assert.Single(rows);
        Assert.Equal("Raindrop.Database", row.AppName);
        Assert.Equal(25, row.MaxItems);
        Assert.True(row.FeatureFlags.EnableNewUI);
        Assert.Equal(30, row.FeatureFlags.RefreshSeconds);
    }

    [Fact]
    public void AddDbConfiguration_InitialLoadFailure_ThrowsByDefault()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ConfigurationBuilder()
                .AddDbConfiguration(new DatabaseConfigOptions
                {
                    BuildConnection = () => throw new InvalidOperationException("connection unavailable"),
                    Sql = "select 1",
                    ReloadOnChange = false
                })
                .Build());

        Assert.Contains("数据库配置加载失败", exception.Message);
        Assert.IsType<InvalidOperationException>(exception.InnerException);
    }

    [Fact]
    public void AddDbConfiguration_InitialLoadFailure_CanBeIgnoredAndObserved()
    {
        Exception? observed = null;

        var configuration = new ConfigurationBuilder()
            .AddDbConfiguration(new DatabaseConfigOptions
            {
                BuildConnection = () => throw new InvalidOperationException("connection unavailable"),
                Sql = "select 1",
                ReloadOnChange = false,
                InitialLoadFailureBehavior = ConfigurationLoadFailureBehavior.Ignore,
                OnLoadException = ex => observed = ex
            })
            .Build();

        Assert.Empty(configuration.AsEnumerable());
        Assert.NotNull(observed);
        Assert.Equal("connection unavailable", observed.Message);
    }

    [Fact]
    public void AddDbConfiguration_InvalidJsonCell_IsPlainTextByDefault()
    {
        var configuration = new ConfigurationBuilder()
            .AddDbConfiguration(new DatabaseConfigOptions
            {
                BuildConnection = _postgres.CreateConnection,
                Sql = "select '{invalid' as json_value",
                SectionName = "Raw",
                ReloadOnChange = false
            })
            .Build();

        Assert.Equal("{invalid", configuration["Raw:0:JsonValue"]);
    }

    [Fact]
    public void AddDbConfiguration_InvalidJsonCell_ThrowsInStrictMode()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ConfigurationBuilder()
                .AddDbConfiguration(new DatabaseConfigOptions
                {
                    BuildConnection = _postgres.CreateConnection,
                    Sql = "select '{invalid}' as json_value",
                    SectionName = "Raw",
                    ReloadOnChange = false,
                    StrictJsonParsing = true
                })
                .Build());

        Assert.Contains("数据库配置加载失败", exception.Message);
    }

    private sealed class DatabaseConfigurationRow
    {
        public string AppName { get; set; } = string.Empty;

        public int MaxItems { get; set; }

        public FeatureFlagsOptions FeatureFlags { get; set; } = new();
    }

    private sealed class FeatureFlagsOptions
    {
        public bool EnableNewUI { get; set; }

        public int RefreshSeconds { get; set; }
    }
}
