using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Raindrop.AppConfiguration;
using Raindrop.AppConfiguration.Abstractions;
using Raindrop.AppConfiguration.Sample.Models;

namespace Raindrop.AppConfiguration.Sample.Scenarios;

public static class LocalJsonConfigurationScenario
{
    public static Task RunAsync()
    {
        Console.WriteLine("===== 本地 JSON 配置示例 =====");

        var builder = Host.CreateApplicationBuilder();

        builder.Configuration
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

        builder.Services.AddAppConfiguration<SampleConfig>(
            builder.Configuration.GetSection("SampleConfig"));

        builder.Services.AddAppConfiguration<CustomBoundConfig>(
            builder.Configuration.GetSection("CustomMapping"),
            (options, configuration) =>
            {
                options.Name = configuration["legacy_app_name"] ?? "default";
                options.Limit = int.TryParse(configuration["legacy_max_items"], out var value) ? value : 10;
            });

        using var host = builder.Build();

        var manager = host.Services.GetRequiredService<IAppConfigurationManager<SampleConfig>>();
        PrintSampleConfig(manager.Config);

        var options = host.Services.GetRequiredService<IOptions<CustomBoundConfig>>();
        Console.WriteLine($"自定义绑定：Name={options.Value.Name}, Limit={options.Value.Limit}");

        var direct = builder.Configuration.GetRequired<SampleConfig>("SampleConfig");
        Console.WriteLine($"直接读取：AppName={direct.AppName}, MaxItems={direct.MaxItems}");
        Console.WriteLine();

        return Task.CompletedTask;
    }

    private static void PrintSampleConfig(SampleConfig config)
    {
        Console.WriteLine($"应用名称：{config.AppName}");
        Console.WriteLine($"最大数量：{config.MaxItems}");
        Console.WriteLine($"数据库连接：{config.Database.ConnectionString}");
        Console.WriteLine($"数据库超时：{config.Database.TimeoutSeconds} 秒");
        Console.WriteLine($"新界面开关：{config.FeatureFlags.EnableNewUI}");
        Console.WriteLine($"日志开关：{config.FeatureFlags.EnableLogging}");
    }
}
