# Raindrop

Raindrop 是一个小型 .NET 微服务框架骨架。

## 文档

- [Raindrop 后端工程开发规范（草案）](docs/engineering-guidelines.md)
- [Raindrop.Data 使用说明](docs/raindrop-data-usage.md)
- [Raindrop.EventBus.Abstractions 设计说明](docs/event-bus-abstractions.md)
- [事件总线 Outbox 与 RabbitMQ 死信重试设计预案](docs/event-bus-outbox-and-rabbitmq-retry-plan.md)
- [工作单元与 RabbitMQ 消费者设计说明](docs/unit-of-work-and-rabbitmq-consumer.md)

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
