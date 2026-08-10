# Raindrop.AppConfiguration — 使用文档

## 概述

`Raindrop.AppConfiguration` 是一个基于 `Microsoft.Extensions.Configuration` 和 `Microsoft.Extensions.Options` 的配置管理模块，提供：

- **强类型配置** — POCO 类绑定配置，类型安全
- **递归 DataAnnotations 验证** — 自动验证嵌套对象的 `[Required]`、`[Range]` 等
- **热重载** — 配置源变化时自动刷新（通过 `IOptionsChangeTokenSource`）
- **错误通知** — 配置加载失败时通知到日志系统
- **Apollo 集成** — 通过 `AddDroplet()` 接入携程 Apollo 配置中心
- **DI 友好** — 通过 `IServiceCollection` 扩展方法注册，与 ASP.NET Core 无缝集成

---

## 架构

```
┌────────────────────────────────────────────────┐
│            你的业务代码                          │
│     IAppConfigurationManager<TOptions>.Config   │
└───────────────────────┬────────────────────────┘
                        │
┌───────────────────────▼────────────────────────┐
│         AppConfigurationManager<TOptions>       │
│  - 持有 IOptionsFactory<TOptions>              │
│  - 监听 IOptionsChangeTokenSource 变化          │
│  - 懒加载 + 线程安全                            │
│  - 错误通知                                     │
└───────────────────────┬────────────────────────┘
                        │
┌───────────────────────▼────────────────────────┐
│      Microsoft.Extensions.Options 管道          │
│  IOptionsFactory → IOptionsChangeTokenSource   │
│  IValidateOptions<TOptions>                    │
└───────────────────────┬────────────────────────┘
                        │
┌───────────────────────▼────────────────────────┐
│      Configuration Source (Json / Apollo / DB)  │
│      Microsoft.Extensions.Configuration        │
└────────────────────────────────────────────────┘
```

---

## 快速开始

### 1. 定义配置类

```csharp
using System.ComponentModel.DataAnnotations;

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

> 推荐使用 `System.ComponentModel.DataAnnotations` 做验证约束。所有嵌套对象也会被递归验证。

### 2. 准备配置文件

**appsettings.json**:
```json
{
  "MyAppConfig": {
    "AppName": "Raindrop",
    "MaxItems": 500,
    "FeatureFlags": {
      "EnableNewUI": true
    }
  }
}
```

### 3. 注册到 DI

```csharp
using Raindrop.AppConfiguration;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddAppConfiguration<MyAppConfig>(
    builder.Configuration.GetSection("MyAppConfig"));

var host = builder.Build();
host.Run();
```

### 4. 消费配置

```csharp
using Raindrop.AppConfiguration.Abstractions;

public class MyService
{
    private readonly MyAppConfig _config;

    public MyService(IAppConfigurationManager<MyAppConfig> manager)
    {
        _config = manager.Config;
    }

    public void Run()
    {
        Console.WriteLine($"App: {_config.AppName}, MaxItems: {_config.MaxItems}");
    }
}
```

---

## 核心 API

### `ServiceCollectionExtensions.AddAppConfiguration<TOptions>()`

两种重载：

| 重载 | 说明 |
|------|------|
| `AddAppConfiguration<TOptions>(this IServiceCollection, IConfiguration)` | 自动绑定，通过反射调用 `IConfiguration.Bind()` 或 `BaseAppConfiguration.Bind()` |
| `AddAppConfiguration<TOptions>(this IServiceCollection, IConfiguration, Action<TOptions, IConfiguration>)` | 自定义绑定逻辑 |

### `IAppConfigurationManager<TOptions>`

```csharp
public interface IAppConfigurationManager<TOptions> where TOptions : class
{
    TOptions Config { get; }
}
```

- `Config` — 懒加载，线程安全，支持热重载

### `ConfigurationExtensions.GetRequired<T>()`

```csharp
// 从 IConfiguration 中获取强类型配置节，不存在则抛出异常
var config = configuration.GetRequired<MyAppConfig>("MyAppConfig");
```

### `OptionsBuilderValidationExtensions.ValidateDataAnnotationsRecursively()`

```csharp
services.AddOptions<MyAppConfig>()
    .Configure(o => { /* ... */ })
    .ValidateDataAnnotationsRecursively()  // 递归验证嵌套对象
    .ValidateOnStart();                     // 启动时验证
```

### `IConfigLoadErrorNotification<TOptions>`

配置加载失败时的错误通知接口：

```csharp
services.AddConfigLoadErrorNotification<MyAppConfig>((sp, ex) =>
{
    var logger = sp.GetRequiredService<ILogger<...>>();
    logger.LogError(ex, "配置加载失败");
});
```

默认实现已经通过 `ILogger` 输出错误日志，通常不需要手动注册。

---

## 使用 BaseAppConfiguration（可选）

如果你的配置类继承 `BaseAppConfiguration<T>`，可以自定义 `Bind` 方法：

```csharp
using Microsoft.Extensions.Configuration;
using Raindrop.AppConfiguration.Abstractions;

public class CustomConfig : BaseAppConfiguration<CustomConfig>
{
    public string Name { get; set; } = string.Empty;
    public int Count { get; set; }

    public override void Bind(IConfiguration config)
    {
        Name = config["custom_name"] ?? "default";
        Count = int.TryParse(config["custom_count"], out var c) ? c : 0;
    }
}
```

---

## Apollo 集成

> Apollo 集成已拆到独立 NuGet 包 `Raindrop.AppConfiguration.Apollo`，
> 按需引用即可。核心模块不依赖 Apollo。

### 用法

```csharp
using Raindrop.AppConfiguration;          // 核心：AddLocalJson
using Raindrop.AppConfiguration.Apollo;   // 按需：AddApollo

var builder = new ConfigurationBuilder()
    .AddLocalJson("Production")          // 1. 本地 JSON + 环境变量
    .AddApollo();                         // 2. 连接 Apollo 远程配置中心
```

**`AddLocalJson`** (核心模块) 一次加载三件事：
1. `Configs/appsettings.json`
2. `Configs/appsettings.{environment}.json`
3. 环境变量

**`AddApollo`** (Apollo 包) 从已加载的 `IConfiguration` 中读取 `"apollo"` 配置节，
连接 Apollo 并挂载 `PublicNamespaces` 中列出的 namespace。

### 本地配置文件 (`Configs/appsettings.json`)

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

支持的文件格式：`.properties`、`.xml`、`.json`、`.yml`、`.yaml`、`.txt`。

### Apollo 选项

`AddApollo()` 接受可选的 section key 参数：
```csharp
.AddApollo("myApolloSection")  // 默认 "apollo"
```

---

## 扩展新配置源

### 数据库配置源

实现一个自定义 `IConfigurationProvider` + `IConfigurationSource`：

```csharp
using Microsoft.Extensions.Configuration;

public class DatabaseConfigurationSource : IConfigurationSource
{
    private readonly string _connectionString;

    public DatabaseConfigurationSource(string connectionString)
    {
        _connectionString = connectionString;
    }

    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        return new DatabaseConfigurationProvider(_connectionString);
    }
}

public class DatabaseConfigurationProvider : ConfigurationProvider
{
    private readonly string _connectionString;
    private Timer? _timer;

    public DatabaseConfigurationProvider(string connectionString)
    {
        _connectionString = connectionString;
    }

    public override void Load()
    {
        // 从数据库加载配置
        Data = new Dictionary<string, string?>
        {
            ["AppName"] = "FromDB",
            ["MaxItems"] = "999"
        };

        // 启动定期刷新
        _timer?.Dispose();
        _timer = new Timer(_ =>
        {
            Load();
            OnReload();  // 触发变化通知
        }, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }
}

// 使用
builder.Configuration.Sources.Add(new DatabaseConfigurationSource(connectionString));
```

### Redis 配置源

基于 Redis pub/sub 的实时配置推送：

```csharp
public class RedisConfigurationProvider : ConfigurationProvider
{
    private readonly ConnectionMultiplexer _redis;

    public RedisConfigurationProvider(string connectionString)
    {
        _redis = ConnectionMultiplexer.Connect(connectionString);
    }

    public override void Load()
    {
        var db = _redis.GetDatabase();
        var hash = db.HashGetAll("app:config");
        Data = hash.ToDictionary(
            e => e.Name.ToString(),
            e => e.Value.ToString());

        // 订阅变化通知
        var sub = _redis.GetSubscriber();
        sub.Subscribe("app:config:changed", (_, _) =>
        {
            Load();
            OnReload();
        });
    }
}
```

### ⚠️ 兼容性说明

`AppConfigurationManager` 通过 `IOptionsChangeTokenSource<TOptions>` 感知配置变化。因此：

- ✅ **任何实现 `IConfigurationSource` + 支持 `IChangeToken` 的源** 都可以与当前模块配合
- ✅ **自定义 `IConfigurationProvider` + `OnReload()`** 可以触发热重载
- ❌ **当前模块没有提供手动刷新 API**（如 `Refresh()`），如果有此需求需要扩展 `AppConfigurationManager`

---

## 项目结构

```
src/
├── Raindrop.AppConfiguration.Abstractions/          # 抽象层
│   ├── IAppConfigurationManager.cs                  # 配置管理器接口
│   └── BaseAppConfiguration.cs                      # 可选的基类
│
└── Raindrop.AppConfiguration/                       # 实现层
    ├── AppConfigurationManager.cs                   # 核心管理器
    ├── ConfigurationBuilderExtensions.cs            # Apollo 集成
    ├── ConfigurationExtensions.cs                   # GetRequired<T>()
    ├── DataAnnotationsValidator.cs                  # 递归验证器
    ├── DataAnnotationsValidateRecursiveOptions.cs   # Options 框架适配
    ├── OptionsBuilderValidationExtensions.cs        # 链式调用扩展
    ├── ServiceCollectionExtensions.cs               # DI 注册
    ├── IConfigLoadErrorNotification.cs              # 错误通知接口
    └── ConfigLoadErrorNotification.cs               # 默认实现

test/
└── Raindrop.AppConfiguration.Tests/                 # 单元测试 + 集成测试
    ├── DataAnnotationsValidatorTests.cs
    ├── ConfigurationExtensionsTests.cs
    └── AppConfigurationManagerIntegrationTests.cs
```

---

## 常见问题

**Q: 为什么容器启动时报错 `Cannot resolve IAppConfigurationManager<TOptions>`？**

A: 确保注册顺序正确：
```csharp
services.AddAppConfiguration<MyConfig>(configuration.GetSection("MyConfig"));
```
且 `TOptions` 类型已正确绑定。

**Q: 如何跳过启动时的配置验证？**

A: 去掉 `.ValidateOnStart()`：
```csharp
services.AddOptions<MyConfig>()
    .Configure(o => conf.Bind(o))
    .ValidateDataAnnotationsRecursively();  // 访问时才验证
```

**Q: 如何自定义错误通知？**

A: 在注册配置前添加：
```csharp
services.AddConfigLoadErrorNotification<MyConfig>((sp, ex) =>
{
    // 自定义处理逻辑，如发送告警邮件
});
```


