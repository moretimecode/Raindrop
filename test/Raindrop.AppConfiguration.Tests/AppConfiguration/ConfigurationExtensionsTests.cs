using Microsoft.Extensions.Configuration;

namespace Raindrop.AppConfiguration.Tests;

public class ConfigurationExtensionsTests
{
    public class TestSection
    {
        public string Key { get; set; } = string.Empty;
        public int Value { get; set; }
    }

    [Fact]
    public void GetRequired_ExistingSection_ReturnsTypedValue()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TestSection:Key"] = "hello",
                ["TestSection:Value"] = "42"
            })
            .Build();

        var result = config.GetRequired<TestSection>("TestSection");

        Assert.NotNull(result);
        Assert.Equal("hello", result.Key);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void GetRequired_MissingSection_Throws()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            config.GetRequired<TestSection>("NonExistentSection"));

        Assert.Contains("NonExistentSection", ex.Message);
    }

    [Fact]
    public void GetRequired_CustomBinding_Works()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AppConfig:AppName"] = "Raindrop",
                ["AppConfig:Version"] = "2.0"
            })
            .Build();

        var result = config.GetRequired<AppConfigurationManagerIntegrationTests.TestAppConfig>("AppConfig");

        Assert.NotNull(result);
        Assert.Equal("Raindrop", result.AppName);
        Assert.Equal("2.0", result.Version);
    }
}
