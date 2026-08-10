using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Raindrop.AppConfiguration;
using Raindrop.AppConfiguration.Abstractions;

namespace Raindrop.AppConfiguration.Tests;

/// <summary>
/// BaseAppConfiguration 自定义 Bind 机制的单元测试。
/// </summary>
public class BaseAppConfigurationTests
{
    // ===================================================================
    //  测试用配置类型
    // ===================================================================

    private sealed class CustomBoundConfig : BaseAppConfiguration<CustomBoundConfig>
    {
        public string Name { get; set; } = string.Empty;
        public int Limit { get; set; }

        public override void Bind(IConfiguration config)
        {
            Name = config["legacy_app_name"] ?? "default";
            Limit = int.TryParse(config["legacy_max_items"], out var value) ? value : 10;
        }
    }

    private sealed class SimpleConfig : BaseAppConfiguration<SimpleConfig>
    {
        public string Value { get; set; } = string.Empty;

        public override void Bind(IConfiguration config)
        {
            Value = config["key"] ?? "fallback";
        }
    }

    // ===================================================================
    //  Bind 方法
    // ===================================================================

    [Fact]
    public void Bind_WithLegacyKeyMapping_MapsCorrectly()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["legacy_app_name"] = "MyApp",
                ["legacy_max_items"] = "256"
            })
            .Build();

        var cfg = new CustomBoundConfig();
        cfg.Bind(config);

        Assert.Equal("MyApp", cfg.Name);
        Assert.Equal(256, cfg.Limit);
    }

    [Fact]
    public void Bind_WithMissingKeys_UsesDefaults()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var cfg = new CustomBoundConfig();
        cfg.Bind(config);

        Assert.Equal("default", cfg.Name);
        Assert.Equal(10, cfg.Limit);
    }

    [Fact]
    public void Bind_SimpleConfig_MapsCorrectly()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["key"] = "test-value"
            })
            .Build();

        var cfg = new SimpleConfig();
        cfg.Bind(config);

        Assert.Equal("test-value", cfg.Value);
    }

    // ===================================================================
    //  通过 ServiceCollection 集成
    // ===================================================================

    [Fact]
    public void AddAppConfiguration_WithBaseAppConfiguration_UsesCustomBind()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["legacy_app_name"] = "ViaDI",
                ["legacy_max_items"] = "512"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();

        services.AddAppConfiguration<CustomBoundConfig>(config);

        var sp = services.BuildServiceProvider();
        var manager = sp.GetRequiredService<IAppConfigurationManager<CustomBoundConfig>>();

        Assert.Equal("ViaDI", manager.Config.Name);
        Assert.Equal(512, manager.Config.Limit);
    }

    [Fact]
    public void AddAppConfiguration_WithCustomBinding_OverridesBind()
    {
        // 自定义 configureOptions 应该优先于 BaseAppConfiguration.Bind
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["legacy_app_name"] = "Original",
                ["legacy_max_items"] = "64"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();

        services.AddAppConfiguration<CustomBoundConfig>(config, (options, _) =>
        {
            options.Name = "Overridden";
            options.Limit = 999;
        });

        var sp = services.BuildServiceProvider();
        var manager = sp.GetRequiredService<IAppConfigurationManager<CustomBoundConfig>>();

        Assert.Equal("Overridden", manager.Config.Name);
        Assert.Equal(999, manager.Config.Limit);
    }

    [Fact]
    public void AddAppConfiguration_NonBaseAppConfiguration_UsesStandardBind()
    {
        // 普通类（不继承 BaseAppConfiguration）使用标准 config.Bind
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AppName"] = "Standard",
                ["Version"] = "2.0"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();

        services.AddAppConfiguration<AppConfigurationManagerIntegrationTests.TestAppConfig>(config);

        var sp = services.BuildServiceProvider();
        var manager = sp.GetRequiredService<IAppConfigurationManager<AppConfigurationManagerIntegrationTests.TestAppConfig>>();

        Assert.Equal("Standard", manager.Config.AppName);
        Assert.Equal("2.0", manager.Config.Version);
    }
}
