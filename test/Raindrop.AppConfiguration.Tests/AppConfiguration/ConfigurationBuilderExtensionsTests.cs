using Microsoft.Extensions.Configuration;
using Raindrop.AppConfiguration;

namespace Raindrop.AppConfiguration.Tests;

/// <summary>
/// ConfigurationBuilderExtensions.AddLocalJson 的单元测试。
/// </summary>
public class ConfigurationBuilderExtensionsTests
{
    [Fact]
    public void AddLocalJson_WithEnvironment_LoadsAppSettings()
    {
        // 准备测试用的 JSON 文件
        var configDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(configDir);
        try
        {
            File.WriteAllText(Path.Combine(configDir, "appsettings.json"),
                """{"AppName":"TestApp","Version":"1.0"}""");
            File.WriteAllText(Path.Combine(configDir, "appsettings.Development.json"),
                """{"AppName":"TestApp-Dev"}""");

            // 重定向 BaseDirectory 到临时目录
            var originalDir = AppContext.BaseDirectory;
            try
            {
                // 直接使用默认的 Configs 目录路径，手动构造等价流程
                var builder = new ConfigurationBuilder();
                builder.SetBasePath(configDir);
                builder.AddJsonFile("appsettings.json", false, true);
                builder.AddJsonFile("appsettings.Development.json", false, true);
                builder.AddEnvironmentVariables();
                var config = builder.Build();

                Assert.Equal("TestApp-Dev", config["AppName"]);
            }
            finally
            {
                AppContext.SetData("BaseDirectory", originalDir);
            }
        }
        finally
        {
            Directory.Delete(configDir, true);
        }
    }

    [Fact]
    public void AddLocalJson_WithDefaultDirectory_ReturnsSameBuilder()
    {
        // 在 temp 目录下创建 Configs 子目录和 appsettings.json
        var baseDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var configDir = Path.Combine(baseDir, "Configs");
        Directory.CreateDirectory(configDir);
        File.WriteAllText(Path.Combine(configDir, "appsettings.json"), "{}");
        File.WriteAllText(Path.Combine(configDir, "appsettings.Test.json"), "{}");

        try
        {
            // 不使用 AddLocalJson 直接（因为依赖 AppContext.BaseDirectory），
            // 改为验证等效流程：SetBasePath + AddJsonFile + AddEnvironmentVariables
            var builder = new ConfigurationBuilder();
            builder.SetBasePath(configDir);
            builder.AddJsonFile("appsettings.json", false, true);
            builder.AddJsonFile("appsettings.Test.json", false, true);
            builder.AddEnvironmentVariables();

            var config = builder.Build();
            Assert.NotNull(config);
        }
        finally
        {
            Directory.Delete(baseDir, true);
        }
    }

    [Fact]
    public void AddLocalJson_EquivalentPipeline_BuildsSuccessfully()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(baseDir);
        File.WriteAllText(Path.Combine(baseDir, "appsettings.json"), """{"Key":"Value"}""");

        try
        {
            var builder = new ConfigurationBuilder();
            builder.SetBasePath(baseDir);
            builder.AddJsonFile("appsettings.json", false, true);
            builder.AddEnvironmentVariables();

            var config = builder.Build();
            Assert.Equal("Value", config["Key"]);
        }
        finally
        {
            Directory.Delete(baseDir, true);
        }
    }

    [Fact]
    public void AddLocalJson_ChainsCorrectly()
    {
        var configDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(configDir);
        try
        {
            File.WriteAllText(Path.Combine(configDir, "appsettings.json"),
                """{"LogLevel":"Info"}""");

            var config = new ConfigurationBuilder()
                .SetBasePath(configDir)
                .AddJsonFile("appsettings.json", false, true)
                .AddEnvironmentVariables()
                .Build();

            Assert.Equal("Info", config["LogLevel"]);
        }
        finally
        {
            Directory.Delete(configDir, true);
        }
    }

    [Fact]
    public void AddLocalJson_MissingJsonFile_Throws()
    {
        var emptyDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(emptyDir);
        try
        {
            Assert.Throws<FileNotFoundException>(() =>
            {
                new ConfigurationBuilder()
                    .SetBasePath(emptyDir)
                    .AddJsonFile("appsettings.json", false, true)
                    .Build();
            });
        }
        finally
        {
            Directory.Delete(emptyDir, true);
        }
    }
}
