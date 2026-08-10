---
name: add-config
description: 在 Raindrop 项目中添加新的配置项或配置类，包括强类型配置定义、DataAnnotations 验证、DI 注册和热重载。当用户需要添加应用配置、定义配置 POCO、或接入新配置源（JSON/数据库/Apollo）时使用。触发词：添加配置、配置项、AppConfiguration、appsettings、配置管理、配置验证。
---

# 添加配置（AppConfiguration）

在 Raindrop 项目中定义强类型配置、注册到 DI 并启用热重载和验证。

## 前置知识

参考文档：
- [docs/app-configuration-usage.md](docs/app-configuration-usage.md) — 配置管理完整使用文档
- [docs/database-app-configuration-usage.md](docs/database-app-configuration-usage.md) — 数据库配置源
- [project-overview](.claude/skills/raindrop-project-overview/SKILL.md) — 项目架构总览

## 步骤

### 1. 定义配置 POCO 类

```csharp
using System.ComponentModel.DataAnnotations;

public class MyServiceConfig
{
    [Required]
    public string ServiceUrl { get; set; } = string.Empty;

    [Range(1, 60)]
    public int TimeoutSeconds { get; set; } = 30;

    [Required]
    public RetryConfig Retry { get; set; } = new();
}

public class RetryConfig
{
    [Range(1, 10)]
    public int MaxRetries { get; set; } = 3;

    [Range(100, 10000)]
    public int DelayMs { get; set; } = 1000;
}
```

**建议使用 `System.ComponentModel.DataAnnotations` 做验证约束。嵌套对象会被递归验证，无需额外配置。**

### 2. 在 appsettings.json 中添加配置节

```json
{
  "MyService": {
    "ServiceUrl": "https://api.example.com",
    "TimeoutSeconds": 15,
    "Retry": {
      "MaxRetries": 5,
      "DelayMs": 2000
    }
  }
}
```

### 3. 注册到 DI

```csharp
// Program.cs
using Raindrop.AppConfiguration;

var builder = Host.CreateApplicationBuilder(args);

// 方式 A：自动绑定 + 启动时验证（推荐）
builder.Services.AddAppConfiguration<MyServiceConfig>(
    builder.Configuration.GetSection("MyService"));

// 方式 B：自定义绑定逻辑
builder.Services.AddAppConfiguration<MyServiceConfig>(
    builder.Configuration.GetSection("MyService"),
    (config, section) =>
    {
        config.ServiceUrl = section["ServiceUrl"] ?? "https://default.example.com";
        config.TimeoutSeconds = int.Parse(section["TimeoutSeconds"] ?? "30");
    });
```

`AddAppConfiguration<T>` 内部完成：

1. `services.AddOptions<T>().Bind(section)` — 绑定配置节
2. `.ValidateDataAnnotationsRecursively()` — 递归验证嵌套对象
3. `.ValidateOnStart()` — 启动时验证，配置错误立即失败
4. 注册 `IConfigLoadErrorNotification<T>` — 默认通过 `ILogger` 输出错误
5. 注册 `IAppConfigurationManager<T>` — 提供懒加载 + 热重载 + 线程安全的 Config 访问

### 4. 消费配置

```csharp
using Raindrop.AppConfiguration.Abstractions;

public class MyService
{
    private readonly MyServiceConfig _config;

    // 注入 IAppConfigurationManager<T>
    public MyService(IAppConfigurationManager<MyServiceConfig> manager)
    {
        _config = manager.Config;  // 懒加载，线程安全，支持热重载
    }

    public async Task CallAsync()
    {
        using var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(_config.TimeoutSeconds)
        };

        await client.GetAsync(_config.ServiceUrl);
    }
}
```

### 5. 自定义验证（可选）

```csharp
public class MyServiceConfig : IValidatableObject
{
    [Required]
    public string ServiceUrl { get; set; } = string.Empty;

    [Range(1, 60)]
    public int TimeoutSeconds { get; set; } = 30;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // 跨字段验证逻辑
        if (ServiceUrl.StartsWith("http") && TimeoutSeconds < 5)
        {
            yield return new ValidationResult(
                "HTTP 请求超时不能少于 5 秒",
                new[] { nameof(TimeoutSeconds) });
        }
    }
}
```

### 6. 使用 BaseAppConfiguration（可选）

如果配置类需要自定义绑定逻辑（如从非标准格式解析），继承 `BaseAppConfiguration<T>`：

```csharp
using Microsoft.Extensions.Configuration;
using Raindrop.AppConfiguration.Abstractions;

public class CustomConfig : BaseAppConfiguration<CustomConfig>
{
    public string Name { get; set; } = string.Empty;

    public override void Bind(IConfiguration config)
    {
        Name = config["custom_name"] ?? "default";
    }
}
```

### 7. 配置错误处理

默认已自动注册 `ConfigLoadErrorNotification`（通过 `ILogger` 输出错误日志）：

```csharp
// 如需自定义错误通知（如发送告警）
services.AddConfigLoadErrorNotification<MyServiceConfig>((sp, ex) =>
{
    var logger = sp.GetRequiredService<ILogger<Program>>();
    logger.LogCritical(ex, "关键配置加载失败: {ConfigType}", typeof(MyServiceConfig).Name);
    // 可在这里添加告警通知
});
```

### 8. 扩展新配置源（可选）

#### 连接 Apollo 配置中心

```csharp
using Raindrop.AppConfiguration;
using Raindrop.AppConfiguration.Apollo;

var builder = new ConfigurationBuilder()
    .AddLocalJson("Production")   // 本地 JSON + 环境变量
    .AddApollo();                   // Apollo 远程配置中心
```

`Configs/appsettings.json` 配置 Apollo 连接：

```json
{
  "apollo": {
    "MetaServer": "http://apollo.meta.server:8080",
    "AppId": "YourAppId",
    "Cluster": "default",
    "PublicNamespaces": "namespace1.json,namespace2.yml"
  }
}
```

#### 连接数据库配置源

```csharp
using Raindrop.AppConfiguration.Database;

var configuration = new ConfigurationBuilder()
    .AddDbConfiguration(new DatabaseConfigOptions
    {
        BuildConnection = () => new NpgsqlConnection(connectionString),
        Sql = "SELECT app_name, max_items FROM app_configurations ORDER BY id",
        SectionName = "AppConfig",
        ReloadOnChange = true,
        ReloadFailureBehavior = ConfigurationLoadFailureBehavior.KeepLastKnownGood
    })
    .Build();
```

### 9. 编写单元测试

```csharp
[Fact]
public void AddAppConfiguration_ValidSection_BindsCorrectly()
{
    var config = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MyService:ServiceUrl"] = "https://test.example.com",
            ["MyService:TimeoutSeconds"] = "20",
            ["MyService:Retry:MaxRetries"] = "5",
            ["MyService:Retry:DelayMs"] = "1000"
        })
        .Build();

    var services = new ServiceCollection();
    services.AddAppConfiguration<MyServiceConfig>(
        config.GetSection("MyService"));

    var provider = services.BuildServiceProvider();
    var manager = provider.GetRequiredService<IAppConfigurationManager<MyServiceConfig>>();

    Assert.Equal("https://test.example.com", manager.Config.ServiceUrl);
    Assert.Equal(20, manager.Config.TimeoutSeconds);
    Assert.Equal(5, manager.Config.Retry.MaxRetries);
}

[Fact]
public void ValidateDataAnnotations_MissingRequired_Fails()
{
    var services = new ServiceCollection();
    var config = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MyService:TimeoutSeconds"] = "20"  // 缺少 ServiceUrl
        })
        .Build();

    services.AddOptions<MyServiceConfig>()
        .Bind(config.GetSection("MyService"))
        .ValidateDataAnnotationsRecursively()
        .ValidateOnStart();

    // 解析或 ValidateOnStart 会触发验证失败
    Assert.Throws<OptionsValidationException>(() =>
    {
        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IOptions<MyServiceConfig>>().Value;
    });
}
```

## 检查清单

- [ ] 配置 POCO 类定义了 DataAnnotations 验证特性
- [ ] 在 `appsettings.json` 中添加了对应的配置节
- [ ] 使用 `AddAppConfiguration<T>()` 注册（自动含验证 + 热重载）
- [ ] 业务代码通过 `IAppConfigurationManager<T>` 消费配置
- [ ] 配置值通过 `manager.Config` 访问（而非 `IOptions<T>.Value`）
- [ ] 编写了单元测试

## 常见错误

- ❌ 直接使用 `IOptions<T>` 取值后缓存 — 不支持热重载，应用 `IAppConfigurationManager<T>.Config`
- ❌ 忘记给嵌套对象属性 new 默认值 — 导致 `Required` 验证报空引用
- ❌ 配置文件不部署到输出目录 — 确保 `CopyToOutputDirectory` 为 `PreserveNewest` 或 `Always`
- ❌ 数据库配置源忘记设置 `ReloadFailureBehavior` — 首次加载失败会抛异常
