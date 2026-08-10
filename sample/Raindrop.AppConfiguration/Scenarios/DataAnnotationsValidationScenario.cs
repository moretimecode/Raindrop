using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Raindrop.AppConfiguration;
using Raindrop.AppConfiguration.Abstractions;

namespace Raindrop.AppConfiguration.Sample.Scenarios;

/// <summary>
/// 演示 DataAnnotations 递归验证：嵌套对象、集合元素都会被校验。
/// </summary>
public static class DataAnnotationsValidationScenario
{
    public static Task RunAsync()
    {
        Console.WriteLine("===== DataAnnotations 递归验证示例 =====");

        // —— 有效配置 ——
        Console.WriteLine("[有效配置]");
        RunWithConfig(new Dictionary<string, string?>
        {
            ["AppName"] = "ValidApp",
            ["MaxItems"] = "50",
            ["Database:ConnectionString"] = "Host=localhost",
            ["Database:TimeoutSeconds"] = "30"
        });

        // —— 无效配置：AppName 为空 ——
        Console.WriteLine("[无效配置：AppName 为空]");
        try
        {
            RunWithConfig(new Dictionary<string, string?>
            {
                ["AppName"] = "",
                ["MaxItems"] = "50"
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  验证失败（预期内）：{ex.InnerException?.Message ?? ex.Message}");
        }

        // —— 无效配置：MaxItems 超出范围 ——
        Console.WriteLine("[无效配置：MaxItems 超出范围]");
        try
        {
            RunWithConfig(new Dictionary<string, string?>
            {
                ["AppName"] = "Test",
                ["MaxItems"] = "99999"
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  验证失败（预期内）：{ex.InnerException?.Message ?? ex.Message}");
        }

        Console.WriteLine();
        return Task.CompletedTask;
    }

    private static void RunWithConfig(Dictionary<string, string?> values)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAppConfiguration<SampleConfig>(config.GetSection("AppConfig") != null
            ? config
            : new ConfigurationBuilder().AddInMemoryCollection(values).Build());

        // 直接使用 IOptions 验证
        services.AddOptions<SampleConfig>()
            .Bind(config)
            .ValidateDataAnnotationsRecursively()
            .ValidateOnStart();

        var sp = services.BuildServiceProvider();

        // 这行在配置无效时会抛异常（ValidateOnStart）
        var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<SampleConfig>>();
        Console.WriteLine($"  AppName={options.Value.AppName}, MaxItems={options.Value.MaxItems}");
    }

    public class SampleConfig
    {
        [Required(ErrorMessage = "AppName 不能为空")]
        [StringLength(50, MinimumLength = 2)]
        public string AppName { get; set; } = string.Empty;

        [Range(1, 10000, ErrorMessage = "MaxItems 必须在 1 到 10000 之间")]
        public int MaxItems { get; set; } = 100;

        public DatabaseConfig Database { get; set; } = new();
    }

    public class DatabaseConfig
    {
        [Required(ErrorMessage = "ConnectionString 不能为空")]
        public string ConnectionString { get; set; } = string.Empty;

        [Range(1, 300)]
        public int TimeoutSeconds { get; set; } = 30;
    }
}
