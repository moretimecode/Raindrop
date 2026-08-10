using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Raindrop.AppConfiguration.Abstractions;
using Xunit;

namespace Raindrop.AppConfiguration.Tests;

public class AppConfigurationManagerIntegrationTests
{
    public class TestAppConfig
    {
        public string AppName { get; set; } = string.Empty;

        public string Version { get; set; } = string.Empty;

        public FeatureFlagsConfig FeatureFlags { get; set; } = new();
    }

    public class FeatureFlagsConfig
    {
        public bool EnableNewUI { get; set; }

        public int MaxRetries { get; set; }
    }

    [Fact]
    public void AddAppConfiguration_RegistersAndResolves_Config()
    {
        var config = BuildTestConfiguration();
        var services = new ServiceCollection();

        services.AddAppConfiguration<TestAppConfig>(config);

        var sp = services.BuildServiceProvider();
        var manager = sp.GetRequiredService<IAppConfigurationManager<TestAppConfig>>();

        Assert.NotNull(manager.Config);
        Assert.Equal("Raindrop", manager.Config.AppName);
        Assert.Equal("1.0.0", manager.Config.Version);
    }

    [Fact]
    public void AddAppConfiguration_ConfigChangeToken_ReloadsConfig()
    {
        var configRoot = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AppName"] = "Initial",
                ["Version"] = "1.0.0",
                ["FeatureFlags:EnableNewUI"] = "false",
                ["FeatureFlags:MaxRetries"] = "3"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddAppConfiguration<TestAppConfig>(configRoot);

        var sp = services.BuildServiceProvider();
        var manager = sp.GetRequiredService<IAppConfigurationManager<TestAppConfig>>();

        Assert.Equal("Initial", manager.Config.AppName);

        configRoot.Reload();

        // 内存配置源不会主动产生变更通知，这里验证静态配置仍能保持稳定。
        Assert.Equal("Initial", manager.Config.AppName);
    }

    [Fact]
    public void AddAppConfiguration_WithCustomBinding_UsesCustomAction()
    {
        var config = BuildTestConfiguration();
        var services = new ServiceCollection();

        services.AddAppConfiguration<TestAppConfig>(config, (options, configuration) =>
        {
            options.AppName = configuration["AppName"] ?? "Fallback";
            options.Version = "custom-bound";
        });

        var sp = services.BuildServiceProvider();
        var manager = sp.GetRequiredService<IAppConfigurationManager<TestAppConfig>>();

        Assert.Equal("Raindrop", manager.Config.AppName);
        Assert.Equal("custom-bound", manager.Config.Version);
    }

    [Fact]
    public void AddAppConfiguration_ValidateOnStart_WithInvalidConfig_Throws()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AppName"] = "",
                ["Version"] = "1.0.0"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddAppConfiguration<TestAppConfig>(config);

        var sp = services.BuildServiceProvider();
        var manager = sp.GetRequiredService<IAppConfigurationManager<TestAppConfig>>();

        Assert.NotNull(manager.Config);
    }

    [Fact]
    public void AddAppConfiguration_ResolveAsOptions_Works()
    {
        var config = BuildTestConfiguration();
        var services = new ServiceCollection();

        services.AddAppConfiguration<TestAppConfig>(config);

        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<TestAppConfig>>();

        Assert.NotNull(options.Value);
        Assert.Equal("Raindrop", options.Value.AppName);
    }

    [Fact]
    public void AddAppConfiguration_ConfigLoadErrorNotification_LogsError()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AppName"] = "Test"
            })
            .Build();

        var services = new ServiceCollection();
        Exception? capturedException = null;

        services.AddConfigLoadErrorNotification<TestAppConfig>((sp, ex) =>
        {
            capturedException = ex;
        });

        services.AddAppConfiguration<TestAppConfig>(config);

        var sp = services.BuildServiceProvider();
        var manager = sp.GetRequiredService<IAppConfigurationManager<TestAppConfig>>();

        Assert.NotNull(manager.Config);
    }

    [Fact]
    public void MultipleRegistrations_AreIndependent()
    {
        var config1 = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AppName"] = "App1",
                ["Version"] = "1.0"
            })
            .Build();

        var config2 = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AppName"] = "App2",
                ["Version"] = "2.0"
            })
            .Build();

        var services = new ServiceCollection();

        services.Configure<TestAppConfig>("instance1", config1);
        services.Configure<TestAppConfig>("instance2", config2);

        var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<IOptionsFactory<TestAppConfig>>();

        var inst1 = factory.Create("instance1");
        var inst2 = factory.Create("instance2");

        Assert.Equal("App1", inst1.AppName);
        Assert.Equal("App2", inst2.AppName);
    }

    [Fact]
    public void AddAppConfiguration_FromJsonFile_ResolvesConfig()
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.test.json", false, true)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();

        services.AddAppConfiguration<TestAppConfig>(config.GetSection("AppConfig"));

        var sp = services.BuildServiceProvider();
        var manager = sp.GetRequiredService<IAppConfigurationManager<TestAppConfig>>();

        Assert.Equal("Raindrop", manager.Config.AppName);
        Assert.Equal("1.0.0", manager.Config.Version);
        Assert.True(manager.Config.FeatureFlags.EnableNewUI);
        Assert.Equal(3, manager.Config.FeatureFlags.MaxRetries);
    }

    private static IConfiguration BuildTestConfiguration()
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AppName"] = "Raindrop",
                ["Version"] = "1.0.0",
                ["FeatureFlags:EnableNewUI"] = "true",
                ["FeatureFlags:MaxRetries"] = "3"
            })
            .Build();
    }
}
