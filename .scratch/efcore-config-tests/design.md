# EF Core 配置测试

## 测试目标

覆盖 `Raindrop.AppConfiguration.Database.EFCore` 包中所有 API，包括单元测试和集成测试。

## 单元测试（纯逻辑，不依赖数据库）

| 文件 | 用例数 | 覆盖 |
|------|--------|------|
| `EntityMapperTests.cs` | 7 | `DelegateEntityMapper`（构造、Map、空列表）；`MapsterEntityMapper`（全局配置、自定义配置、多实体、null 校验） |
| `DefaultConfigurationKeyGeneratorTests.cs` | 14 | 简单属性、嵌套对象、null 值/属性、空对象、列表展平（含嵌套）、DateTime/Guid/Nullable、大小写不敏感 |
| `EntityComparerTests.cs` | 9 | Equals（同引用、同值、差异字段、null 各种组合）、GetHashCode（一致性、差异性、null 返回 0） |
| `EFConfigurationOptionsTests.cs` | 17 | 所有默认值、所有 With* 方法、Mapper/Mapster 互斥、OnLoadFailed、Fluent 链式调用 |
| `EFConfigurationExtensionsTests.cs` | 10 | 简化重载（含默认 SectionName）、Fluent Builder 重载、Mapster 路径、ChangeDetector、参数校验（3 种缺失场景）、全配置组合 |

## 集成测试（依赖 PostgreSQL）

| 文件 | 用例数 | 覆盖 |
|------|--------|------|
| `EfConfigurationProviderIntegrationTests.cs` | 4 | Load+Map、初始加载失败默认 Throw、**热重载（DB 变更后自动刷新）**、**KeepLastKnownGood（刷新失败保留旧数据）** |

## 发现的问题

`EFConfigurationProvider` 构造函数和 `ConfigurationRoot` 构造函数各自调用了一次 `Load()`，导致初始化时数据被加载两遍（factory/DB 连接打开两次）。这不是测试引入的问题，是 Provider 设计层面的冗余，后续应修复。
