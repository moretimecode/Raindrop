---
name: raindrop-config
description: Raindrop 配置管理。当用户需要强类型配置、DataAnnotations 递归验证、热重载、Apollo 配置中心或数据库配置源时使用。
---

# Raindrop.AppConfiguration — 配置管理

## NuGet 包

```
Raindrop.AppConfiguration.Abstractions       — 抽象
Raindrop.AppConfiguration                     — 核心
Raindrop.AppConfiguration.Apollo              — Apollo 配置中心
Raindrop.AppConfiguration.Database            — 数据库配置源
Raindrop.AppConfiguration.Database.EFCore     — EF Core Provider
```

## 定义配置类

```csharp
public class MyAppConfig
{
    [Required]
    public string AppName { get; set; } = string.Empty;

    [Range(1, 9999)]
    public int MaxItems { get; set; } = 100;

    public FeatureFlagsConfig FeatureFlags { get; set; } = new();
}

public class FeatureFlagsConfig
{
    public bool EnableNewUI { get; set; }
}
```

## DI 注册

```csharp
builder.Services.AddAppConfiguration<MyAppConfig>(
    builder.Configuration.GetSection("MyAppConfig"));
```

## 消费配置

```csharp
public class MyService
{
    public MyService(IAppConfigurationManager<MyAppConfig> manager)
    {
        var config = manager.Config;  // 懒加载，线程安全，热重载
    }
}
```

## 核心 API

### 递归验证

```csharp
services.AddOptions<MyAppConfig>()
    .Configure(o => { ... })
    .ValidateDataAnnotationsRecursively()  // 包含嵌套对象
    .ValidateOnStart();                     // 启动即验证
```

### 强类型获取

```csharp
var config = configuration.GetRequired<MyAppConfig>("MyAppConfig");
```

### 错误通知

```csharp
services.AddConfigLoadErrorNotification<MyAppConfig>((sp, ex) =>
{
    var logger = sp.GetRequiredService<ILogger<Program>>();
    logger.LogError(ex, "配置加载失败");
});
```

## BaseAppConfiguration（可选）

```csharp
public class CustomConfig : BaseAppConfiguration<CustomConfig>
{
    public string Name { get; set; } = string.Empty;

    public override void Bind(IConfiguration config)
    {
        Name = config["custom_name"] ?? "default";
    }
}
```

## Apollo 集成

```csharp
var builder = new ConfigurationBuilder()
    .AddLocalJson("Production")     // Configs/appsettings.json + 环境变量
    .AddApollo();                   // 读取 "apollo" 节连接

// appsettings.json:
{
  "apollo": {
    "MetaServer": "http://apollo.meta.server:8080",
    "AppId": "YourAppId",
    "Cluster": "default",
    "PublicNamespaces": "namespace1.json,namespace2.yml"
  }
}
```

## 数据库配置源

```csharp
builder.Configuration.AddDatabase(options =>
{
    options.ConnectionString = "Host=localhost;...";
    options.RefreshInterval = TimeSpan.FromMinutes(5);
});
```

## 扩展新配置源

任何实现 `IConfigurationSource` + `IChangeToken` 的源都能配合。步骤：

1. 实现 `IConfigurationSource` / `IConfigurationProvider`
2. `Load()` 填充 Data 字典
3. 定期/事件驱动调用 `OnReload()`
4. 提供 `Add*()` 扩展方法

## 注册规则

- `IAppConfigurationManager<T>` — Scoped
- 首次访问 `.Config` 时触发验证（`ValidateOnStart()` 可在启动时提前验证）
- `IOptionsChangeTokenSource<T>` 驱动热重载
