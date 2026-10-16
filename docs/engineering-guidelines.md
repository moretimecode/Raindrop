# Raindrop 后端工程开发规范（草案）


> 本文是 Raindrop 仓库以及基于 Raindrop 创建的 .NET 微服务的工程规范草案。
> `AGENTS.md` 适合只放红线和指针，细则统一维护在本文。
>
> 当前技术栈：.NET 10 / ASP.NET Core / Raindrop.CQRS(MediatR) / Raindrop.Data(EF Core) / RabbitMQ / Redis(待沉淀包) / Raindrop.AppConfiguration / OpenTelemetry。

基础依赖版本集中维护在根目录 `Directory.Build.props`：EF Core 与 Microsoft 扩展库为 10.0.13，Npgsql 与 PostgreSQL Provider 为 10.0.3，Mapster 为 10.0.13 稳定版，RabbitMQ.Client 为 7.2.2，FluentValidation 为 12.2.0。OpenTelemetry SDK／Exporter／Hosting 为 1.19.1，ASP.NET Core instrumentation 为 1.19.0。测试使用 Microsoft.NET.Test.Sdk 18.11.0、coverlet.collector 10.1.0、xUnit 2.9.3 和 Visual Studio runner 3.1.5。Apollo Configuration 为 2.11.0；内嵌 MediatR 源码基线为 12.5.0。

## §0 黄金三条

1. **依赖向内**：领域模型必须纯净。领域对象只依赖稳定抽象，例如 `Raindrop.Data`、`Raindrop.UnitOfWork.Abstractions` 和项目自己的领域接口；EF Core、RabbitMQ、Redis、HTTP、OpenTelemetry SDK 等具体实现不进入领域层。
2. **注入归位**：框架能力通过各 Raindrop 包自己的 `ServiceCollectionExtensions` 暴露；业务项目的 `Program.cs` 只做编排。能通过 Raindrop 扩展方法表达的注册，不在 Controller、Handler 或普通业务服务里散落 `services.AddX`。
3. **横切交给框架**：异常、统一响应、访问日志、工作单元、事件消费、OpenTelemetry 导出等横切能力由 Raindrop 管道或扩展统一处理。业务代码不手拼返回信封，不吞异常转 200，不在用例代码里到处手写事务边界。

## §0.1 待讨论项

以下内容需要结合 Raindrop 的真实目标再定，不建议直接照搬原规范：

1. **业务服务模板是否进入仓库**：当前 Raindrop 是框架骨架，`sample/` 只是包级示例。是否需要新增一个标准业务服务模板，采用 `Host/Application/Domain/Infrastructure/Common` 分层？
2. **通用仓储的定位**：原规范禁止 `IRepository<T>`，但 Raindrop.Data 当前提供 `IRepository<TEntity, TKey>` 作为框架能力。建议框架层保留通用仓储，业务服务遇到复杂聚合时再定义专用仓储接口。
3. **HTTP 自动工作单元**：RabbitMQ 消费侧已经支持 `[UnitOfWork]`，HTTP 侧文档里有规划，但当前主要能力仍在 ASP.NET Core 管道和手动 `IUnitOfWorkFactory`。是否要实现 HTTP `[UnitOfWork]` Filter/EndpointFilter？
4. **gRPC 是否作为一等能力**：原规范强覆盖 gRPC 和 proto 真理源；Raindrop 当前没有 gRPC 包。可以作为可选扩展，而不是核心红线。
5. **门禁脚本是否补齐**：`AGENTS.md` 引用了 `docs/agents/*`，但仓库当前没有这些文件；Makefile 目前主要负责 pack/push，没有串联依赖检查、质量检查和测试。是否要新增 `make check`、`make verify`？
6. **错误码集中管理**：Raindrop.Utility 有 `FriendlyException`，但没有项目级 `ErrorCode` 模板。业务服务是否统一要求 `Common/ErrorCode.cs`？
7. **配置根策略**：Raindrop.AppConfiguration 支持配置管理。业务服务是否要求单一应用配置根，例如 `AppConfiguration`，而框架包内部配置继续使用 package options/internal options？
8. **SQL 管理策略**：当前框架仓库没有统一 `sql/` 目录，样例也以演示为主。业务服务是否采用 `sql/vX.Y.sql + sql/update.sql`，还是使用 EF Migration？
9. **Redis 与分布式锁包**：仓库里还没有 `Raindrop.Redis` 或 RedLock 封装。相关规范先作为应用建议，等包沉淀后再升级为红线。
10. **发布版本来源**：当前 `make.ps1` 和 `Makefile` 固定 `0.0.1`。是否改成由 `Directory.Build.props`、tag 或 CHANGELOG 首行驱动？

## §1 仓库结构与包边界

当前 Raindrop 仓库结构：

```text
Raindrop.slnx
├─ src/
│  ├─ MediatR
│  ├─ Raindrop.Utility
│  ├─ Raindrop.Data
│  ├─ Raindrop.Data.EFCore
│  ├─ Raindrop.UnitOfWork.Abstractions
│  ├─ Raindrop.EventBus.Abstractions
│  ├─ Raindrop.EventBus.Core
│  ├─ Raindrop.EventBus.MediatR
│  ├─ Raindrop.EventBus.RabbitMQ
│  ├─ Raindrop.CQRS
│  ├─ Raindrop.CQRS.FluentValidation
│  ├─ Raindrop.AspNetCore
│  ├─ Raindrop.Observability.Abstractions
│  └─ Raindrop.AppConfiguration.*
├─ test/
├─ sample/
├─ docs/
└─ nuget-local/
```

包依赖规则：

| 包类型 | 示例 | 规则 |
|---|---|---|
| 基础抽象 | `Raindrop.Data`、`Raindrop.UnitOfWork.Abstractions`、`Raindrop.Observability.Abstractions` | 不依赖具体基础设施，不引用 ASP.NET Core、EF Provider、RabbitMQ Client |
| 核心编排 | `Raindrop.EventBus.Core`、`Raindrop.CQRS` | 依赖抽象和必要的轻量框架，不依赖具体适配器 |
| 具体适配器 | `Raindrop.Data.EFCore`、`Raindrop.EventBus.RabbitMQ`、`Raindrop.EventBus.MediatR` | 依赖核心抽象，实现具体落地能力 |
| Web 集成 | `Raindrop.AspNetCore` | 负责应用入口集成、HTTP 管道和 OpenTelemetry SDK 注册 |
| 扩展集成 | `Raindrop.CQRS.FluentValidation`、`Raindrop.AppConfiguration.*` | 只把第三方能力接入 Raindrop 抽象，不反向污染核心包 |

红线：

- 抽象包不引用具体适配器。
- 具体适配器不反向引用 Web 包。
- `Raindrop.AspNetCore` 可以作为应用入口集成包，但不应该承载事件总线、CQRS、数据访问的核心实现。
- 新增包前先明确它是抽象、核心、适配器还是入口集成，避免出现“大杂烩”包。

## §1.1 基于 Raindrop 的业务服务推荐结构

如果用 Raindrop 创建业务微服务，推荐结构如下：

```text
<Service>.sln
├─ src/
│  ├─ <Service>.Host             Program、DI 编排、中间件、HostedService
│  ├─ <Service>.Application      Controller、Command、Query、DTO、集成事件处理器
│  ├─ <Service>.Domain           聚合、实体、值对象、领域事件、领域服务、领域接口
│  ├─ <Service>.Infrastructure   EF Core、仓储实现、RabbitMQ/Redis/远程服务适配
│  └─ <Service>.Common           ErrorCode、应用配置、跨层常量
├─ test/
├─ docs/
└─ sql/ 或 migrations/
```

依赖方向：

| 工程 | 允许依赖 | 禁止 |
|---|---|---|
| Host | Application / Infrastructure / Domain / Common / Raindrop.AspNetCore | 写业务规则 |
| Application | Domain / Common / Raindrop.CQRS | 直接引用 DbContext、EF Provider、RabbitMQ Client、Redis Client |
| Infrastructure | Domain / Common / Raindrop.Data.EFCore / Raindrop.EventBus.* | 反向依赖 Application |
| Domain | Common / Raindrop.Data / Raindrop.UnitOfWork.Abstractions | 引用 EF、ASP.NET Core、RabbitMQ、Redis、OpenTelemetry SDK |
| Common | 无业务依赖 | 依赖上层业务工程 |

## §1.2 文件与目录命名

- 一个公共类型一个文件，文件名等于类型名。
- 聚合根、实体、命令、查询、处理器、DTO、配置对象都放到表达职责的目录里。
- 公共类型和成员使用 PascalCase，局部变量和参数使用 camelCase，异步方法以 `Async` 结尾。
- 文档注释和协作说明必须使用中文。
- 保持 nullable reference types 启用，新增代码不得引入未处理的 nullable 警告。

## §2 DI 装配

Raindrop 框架包的注册入口：

| 能力 | 入口 |
|---|---|
| ASP.NET Core 管道 | `AddRaindropAspNetCore()` / `UseRaindropAspNetCore()` |
| OpenTelemetry | `AddRaindropOpenTelemetry(configuration)` |
| CQRS | `AddRaindropCQRS(applicationAssemblies)` |
| FluentValidation | `AddValidation()` |
| EF Core 数据访问 | `AddRaindropData<TDbContext>(...)` |
| Outbox 同事务登记 | `AddRaindropOutbox<TDbContext>(...)`，配合模型映射 |
| 本地事件总线 | `AddMediatRLocalEventBus(...)` |
| RabbitMQ 事件总线 | `AddRabbitMqEventBus(...)` |
| 应用配置 | `AddAppConfiguration(...)` 相关扩展 |

业务项目中的约定：

- `Program.cs` 或 `Host/ServiceCollectionExtensions.cs` 只编排这些扩展方法。
- `Infrastructure` 负责注册 EF Core、Redis、远程服务、具体事件总线适配器。
- `Host` 负责注册 ASP.NET Core 管道、OpenTelemetry、Swagger、认证授权等入口能力。
- Handler、Controller、领域服务不直接操作 `IServiceCollection`。
- 框架包新增配置时，优先使用 options 类型；仅包内部使用的配置类可以是 `internal`，但用于配置绑定的属性需要有可写 setter。

## §3 配置与连接

通用规则：

- 生产代码禁止硬编码连接串、账号、密码和平台地址。
- 本地测试连接可以放在 `AGENTS.md` 或测试夹具中，但不得进入业务默认配置。
- `appsettings.{Environment}.json` 只放结构和非敏感项；敏感值走环境变量、配置中心或部署平台。
- 环境名读取优先级统一为 `ASPNETCORE_ENVIRONMENT`，其次 `DOTNET_ENVIRONMENT`，最后 `Production`。

Raindrop OpenTelemetry 配置：

```json
{
  "Raindrop": {
    "OpenTelemetry": {
      "ServiceName": "order-api",
      "ServiceNamespace": "raindrop",
      "ServiceVersion": "1.0.0",
      "ServiceInstanceId": "",
      "AutoGenerateServiceInstanceId": true,
      "OtlpEndpoint": "http://localhost:4317"
    }
  }
}
```

规则：

- `ServiceName` 和合法 `OtlpEndpoint` 同时存在时才注册 OpenTelemetry SDK。
- 配置缺失时跳过 SDK 注册，但 `ActivitySource` 埋点代码仍保留。
- `service.namespace` 不等于环境名；环境名写入 `deployment.environment.name`。

## §4 横切能力

ASP.NET Core 管道由 `UseRaindropAspNetCore()` 固定编排：

```text
AccessLogMiddleware
  RaindropExceptionHandlingMiddleware
    StatusCodeResponseMiddleware
      MVC / Minimal API
```

MVC 结果包装和 Action 参数采集由 Filter 完成：

- `ApiResponseResultFilter`：统一响应信封。
- `AccessLogFilter`：采集 MVC Action 入参和可选响应对象。

红线：

- Controller 和 Minimal API 不手拼 `{ code, message, data, success }`。
- 业务代码不 catch 后返回成功响应。
- 可预期业务失败使用 `FriendlyException`，并携带稳定错误码。
- AccessLog 写入统一走 `IAccessLogWriter`，默认实现使用 `ILogger`。
- OpenTelemetry 导出只在应用入口配置，不在业务代码里判断“平台有没有启用”。

工作单元：

- 手动事务入口使用 `IUnitOfWorkFactory.BeginAsync()`。
- RabbitMQ 消费侧使用 handler 上的 `[UnitOfWork]` 显式触发自动工作单元。
- HTTP 自动工作单元仍属于待讨论能力，不应在规范里假设已经完成。

## §5 错误码

Raindrop 提供 `FriendlyException`，业务服务应自行集中错误码：

```csharp
public static class ErrorCode
{
    public const int DefaultCode = 1000000;
    public const int OrderNotFound = 1000001;
}
```

规则：

- 可预期业务失败必须有具名错误码。
- 禁止在业务代码里散写魔法数字。
- 禁止只靠 exception message 区分业务错误。
- 框架包自身只定义通用消息和通用异常行为，不替业务服务分配业务错误码段。

## §6 对外接口与 CQRS

HTTP/WebAPI 是当前 Raindrop 的一等入口；gRPC 可作为业务项目自行扩展或后续独立包。

Controller / Minimal API 约定：

- 只做参数接收、轻量映射、派发 Command/Query、返回结果。
- 不写查库、状态流转、金额计算、跨聚合规则。
- 不直接依赖 DbContext、Repository 实现或外部 SDK。

Raindrop.CQRS 约定：

- 写操作定义 `record XxxCommand(...) : ICommand` 或 `ICommand<TResponse>`。
- 读操作定义 `record XxxQuery(...) : IQuery<TResponse>`。
- 处理器继承 `CommandHandler<...>` 或 `QueryHandler<...>`。
- 入口层注入 `ICommandBus` / `IQueryBus`，调用 `Send`。
- 命令、查询、DTO、查询结果优先使用不可变 `record`。

## §7 领域与持久化

领域建模规则：

- 聚合根继承 `AggregateRoot<TKey>` 或 `AggregateRoot`。
- 普通实体继承 `Entity<TKey>`。
- 状态变更和不变量校验写在实体/聚合方法里。
- 聚合属性对外尽量只读，使用 `private set` 或受控方法变更。
- Application 层避免基于实体内部状态写业务分支；如果出现，优先下沉为领域方法。
- Domain Service 只处理必须跨聚合协作的规则，不替代实体行为。

仓储规则：

- Raindrop 框架层保留 `IRepository<TEntity, TKey>`，它是通用持久化能力。
- 业务服务可以直接使用通用仓储处理简单 CRUD。
- 当仓储方法需要表达业务意图、隐藏查询细节或保护聚合边界时，应定义专用仓储接口，例如 `IOrderRepository`。
- 仓储只负责持久化和查询，不写状态流转和业务规则。
- 不向 Application 暴露 `DbContext`、`DbSet`、`IQueryable`。

EF Core 规则：

- EF Core 具体实现放在 Infrastructure 或 `Raindrop.Data.EFCore` 适配包。
- 新增持久化实体必须有 DbContext 注册和映射。
- 原生 SQL 禁止 `SELECT *`，必须显式列字段。
- 批量 SQL 或脚本应优先幂等。

## §7.5 领域事件与集成事件

事件批次、提交结果和资源所有权见[仓储与工作单元](repository-and-unitofwork.md)；本地消息表、事务登记及能力边界见[Outbox](transactional-outbox.md)。组件文档描述当前源码，已发布包是否包含能力以对应版本源码为准。

Raindrop 当前事件基础类型：

| 类型 | 用途 |
|---|---|
| `IEvent` | 领域事件和集成事件共享的基础标记 |
| `IIntegrationEvent : IEvent` | 跨进程或跨服务传递的集成事件 |
| `IEventBus` | 统一发布入口 |
| `IEventHandler<TEvent>` | 事件处理器 |

领域事件：

- 表达本服务内部已经发生的事实。
- 使用不可变 `record`。
- 命名使用过去式，例如 `OrderCreated`、`AccountWithdrawn`。
- 由聚合根在状态变更后产生。
- 处理器需要幂等，只做副作用协调，不塞核心业务规则。

集成事件：

- 凡是要过 MQ 的事件必须实现 `IIntegrationEvent`。
- RabbitMQ 路由使用 `RabbitMqEventAttribute` 声明。
- 本地事件使用 `LocalEventAttribute` 或由 MediatR 本地事件总线补默认本地路由。
- 默认即时发布支持显式声明两种路由；启用 Outbox 时用独立领域事件进行本地协调，集成事件必须只有明确远程目标，不允许混用本地路由。

## §8 消息总线

Raindrop 事件总线分层：

```text
Raindrop.EventBus.Abstractions
  IEventBus / IEventTransport / IEventRouteResolver / IEventHandler<T>

Raindrop.EventBus.Core
  RoutingEventBus / handler 扫描 / 消费幂等基础实现

Raindrop.EventBus.MediatR
  进程内本地事件传输

Raindrop.EventBus.RabbitMQ
  RabbitMQ 路由、发布、消费、死信重试、HostedService
```

规则：

- 业务代码面向 `IEventBus`，不直接依赖 RabbitMQ Client。
- RabbitMQ 消费 handler 需要事务时，用 `[UnitOfWork]` 显式标记。
- 幂等消费优先使用 `IdempotentConsumerAttribute` 和 `IConsumerIdempotencyStore`。
- 没有路由的事件不应默默丢弃；`RoutingEventBus` 应明确失败。

## §9 缓存与分布式锁

当前仓库尚未沉淀 Redis/RedLock 包。业务服务暂按以下建议执行：

- Domain 只定义缓存、锁或外部能力接口。
- Infrastructure 实现 Redis、RedLock 或其他具体适配。
- 缓存 key 必须有服务级前缀，避免跨服务冲突。
- 缓存序列化格式要稳定，优先 `System.Text.Json`。
- 不把 Redis Client 类型穿透到 Application 或 Domain。

## §10 可观测性

日志：

- HTTP access log 统一走 `IAccessLogWriter`。
- 默认 category 为 `Raindrop.AccessLog`，业务项目可按 category 分流。

Tracing：

- 框架包通过 `ActivitySource` 发出埋点。
- `Raindrop.CQRS` 使用 `CQRSActivitySource`。
- `Raindrop.EventBus` 应提供独立 `EventBusActivitySource`。
- SDK 注册统一放在 `Raindrop.AspNetCore` 的 `AddRaindropOpenTelemetry(configuration)`。
- 无遥测平台配置时不注册 SDK，不影响业务执行。

## §11 测试约定

- 测试工程放在 `test/`，命名为 `<Package>.Tests`。
- 新增或修改代码前先补文档和测试用例。
- 框架行为优先写单元测试；涉及 ASP.NET Core 管道、RabbitMQ、EF Core 时补集成测试。
- 业务规则必须有单测。
- AI 生成的业务逻辑必须附带覆盖规则的测试。
- 外部依赖优先用 fake/stub；必要的 PostgreSQL、RabbitMQ、Redis 集成测试要清楚隔离，并可在本地配置下重复运行。

集成测试通过 `RAINDROP_TEST_POSTGRESQL_CONNECTION_STRING` 和 `RAINDROP_TEST_RABBITMQ_CONNECTION_STRING` 指定测试服务，未指定时使用仓库约定的本地连接。RabbitMQ 环境变量同时用于直接消费测试和 HostedService 测试；生产注册的默认连接不受影响。测试前需要确保 PostgreSQL 与 RabbitMQ 可达且账号有测试拓扑的声明、发布和消费权限。隔离测试服务使用独立容器、仅绑定本机端口和临时强密码，避免变更已有服务的账号。

测试用例：配置了 RabbitMQ 连接时使用指定连接；未配置或配置为空时使用本地默认连接。所有数据库测试项目使用指定的 PostgreSQL 连接。现有发布消费、失败重试、死信、仓储、工作单元和配置源集成测试验证端到端行为。

常用验证：

```powershell
dotnet build Raindrop.slnx
dotnet test Raindrop.slnx
dotnet test test\Raindrop.AspNetCore.Tests\Raindrop.AspNetCore.Tests.csproj --no-restore
```

## §12 门禁与发布

当前必须人工执行：

```powershell
dotnet build Raindrop.slnx -c Release
dotnet test Raindrop.slnx
.\make.ps1 build
```

建议后续补齐：

- `make check`：依赖方向、聚合引用、禁止 `SELECT *`、文件长度、God Service 检查。
- `make verify`：`dotnet build` + `dotnet test` + `make check`。
- 版本来源统一：从 `Directory.Build.props`、tag 或 CHANGELOG 读取，不在 Makefile 和 PowerShell 脚本里重复写死。
- `docs/agents/issue-tracker.md`、`docs/agents/triage-labels.md`、`docs/agents/domain.md` 与 `AGENTS.md` 对齐。

## §13 PR Checklist

- [ ] 新增能力先有文档和测试。
- [ ] 包依赖方向符合 §1，没有抽象包依赖具体适配器。
- [ ] DI 注册归位到对应 Raindrop 扩展或业务 Host/Infrastructure 扩展。
- [ ] Controller / Minimal API 无业务规则，走 Command/Query 或应用服务编排。
- [ ] Application 不直接依赖 DbContext、EF Provider、RabbitMQ Client、Redis Client。
- [ ] 领域对象维护自身不变量，不是贫血属性容器。
- [ ] 仓储只做持久化，不写业务规则。
- [ ] 集成事件实现 `IIntegrationEvent`，领域事件和集成事件语义不混用。
- [ ] RabbitMQ 消费事务通过 `[UnitOfWork]` 显式标记。
- [ ] 无硬编码连接串、平台地址、敏感凭据。
- [ ] 可预期业务失败有具名错误码。
- [ ] 无手写返回信封、异常吞掉、裸露基础设施类型。
- [ ] OpenTelemetry 只在入口扩展中注册 SDK，业务埋点只使用 `ActivitySource`。
- [ ] `dotnet build` 和相关 `dotnet test` 已通过。
- [ ] PR 描述说明设计取舍、影响面和验证结果。
