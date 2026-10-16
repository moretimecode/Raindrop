# Raindrop

Raindrop 是一个小型 .NET 微服务框架骨架。

## 文档

| 组件 | 设计与实现 |
|---|---|
| 领域模型 | [实体、聚合根、业务规则与分页](docs/raindrop-data-usage.md) |
| 数据访问与事务 | [仓储、规约、UoW 与事件准备](docs/repository-and-unitofwork.md) |
| CQRS | [命令查询、Bus、Pipeline 与验证](docs/cqrs.md) |
| 事件总线 | [事件、路由、transport 与 Outbox 接缝](docs/event-bus-abstractions.md) |
| RabbitMQ | [即时发布、消费、幂等、重试与死信](docs/rabbitmq-consumer-hostedservice.md) |
| 本地消息表 | [Outbox 模型与同事务登记](docs/transactional-outbox.md) |
| 应用配置 | [强类型配置、验证、热重载与 Apollo](docs/app-configuration-usage.md) |
| 数据库配置源 | [SQL 与 EF Core 配置源](docs/database-app-configuration-usage.md) |
| ASP.NET Core | [HTTP 管道、统一响应与可观测性](docs/aspnetcore-observability.md) |
| 工程规范 | [分层、装配、编码与门禁](docs/engineering-guidelines.md) |

Outbox 当前只完成业务与发送意图同事务保存，启用后消息保持 Pending；后台派发、发布确认和恢复尚未实现。组件文档以当前源码为准，不据此推定已发布 NuGet 包包含相同能力。

## 常用命令

```powershell
dotnet build Raindrop.slnx
dotnet test Raindrop.slnx
```

本地 NuGet 仓库位于 `nuget-local/`，对应 NuGet 源名称为 `raindrop-local`。当前框架包版本为 `0.1.1`；使用 Makefile 时通过 `VERSION` 指定版本：

```bash
make build VERSION=0.1.1
make list-local
```

集成测试需要 PostgreSQL 与 RabbitMQ。使用隔离测试服务时，通过 `RAINDROP_TEST_POSTGRESQL_CONNECTION_STRING` 和 `RAINDROP_TEST_RABBITMQ_CONNECTION_STRING` 指定连接，配置方式见[测试约定](docs/engineering-guidelines.md#11-测试约定)。
