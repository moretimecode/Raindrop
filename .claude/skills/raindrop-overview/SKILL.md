---
name: raindrop-overview
description: 了解 Raindrop 项目整体架构、模块职责、技术栈和编码规范。当需要快速理解项目结构、选择正确的类库、或向新 agent 介绍项目时使用。触发词：项目架构、项目总览、Raindrop 是什么、模块依赖、技术栈。
---

# Raindrop 项目架构总览

## 一句话定义

Raindrop 是一个**小型 .NET 微服务框架骨架**，基于 .NET 10.0，提供 DDD 领域建模、CQRS、事件总线、配置管理和 ASP.NET Core 可观测性等基础能力。

## 分层架构

```text
┌─────────────────────────────────────────────────────────┐
│  应用层 (Application)                                    │
│  Raindrop.CQRS / Raindrop.CQRS.FluentValidation         │
│  命令/查询分离、Pipeline Behavior、自动验证               │
├─────────────────────────────────────────────────────────┤
│  基础设施层 (Infrastructure)                             │
│  Raindrop.Data.EFCore          EF Core 仓储 + 工作单元    │
│  Raindrop.EventBus.RabbitMQ    RabbitMQ 事件总线         │
│  Raindrop.EventBus.MediatR     MediatR 本地事件总线       │
│  Raindrop.AppConfiguration     配置管理（热重载/验证）     │
│  Raindrop.AppConfiguration.Apollo  Apollo 配置中心        │
│  Raindrop.AppConfiguration.Database / .EFCore  数据库配置  │
│  Raindrop.AspNetCore           HTTP 管道（响应/异常/日志） │
├─────────────────────────────────────────────────────────┤
│  抽象层 (Abstractions)                                   │
│  Raindrop.UnitOfWork.Abstractions  工作单元契约           │
│  Raindrop.EventBus.Abstractions    事件总线契约           │
│  Raindrop.EventBus.Core            事件路由引擎           │
│  Raindrop.Observability.Abstractions  可观测性抽象        │
├─────────────────────────────────────────────────────────┤
│  领域层 (Domain)                                         │
│  Raindrop.Data                  实体、聚合根、领域事件标记  │
│  Raindrop.Utility               FriendlyException 等工具  │
└─────────────────────────────────────────────────────────┘
```

## 类库职责速查

| 类库 | 职责 | 关键类型 |
|------|------|----------|
| `Raindrop.Data` | 领域模型基类型 | `IEvent`, `AggregateRoot<TKey>`, `IBusinessRule`, `Pageable` |
| `Raindrop.Data.EFCore` | EF Core 仓储/工作单元 | `IRepository<T,TKey>`, `EntityFrameworkCoreUnitOfWork` |
| `Raindrop.UnitOfWork.Abstractions` | 工作单元契约 | `IUnitOfWork`, `IUnitOfWorkFactory` |
| `Raindrop.EventBus.Abstractions` | 事件总线契约 | `IEventBus`, `IEventHandler<T>`, `EventRouteAttribute` |
| `Raindrop.EventBus.Core` | 路由事件总线引擎 | `RoutingEventBus`, `InMemoryConsumerIdempotencyStore` |
| `Raindrop.EventBus.MediatR` | 本地进程内事件 | `MediatRLocalEventTransport`, `AddMediatRLocalEventBus()` |
| `Raindrop.EventBus.RabbitMQ` | RabbitMQ 事件总线 | `RabbitMqHostedService`, `[RabbitMqEvent]`, 重试/死信 |
| `Raindrop.CQRS` | CQRS 命令查询分离 | `ICommand<T>`, `IQuery<T>`, `ICommandBus`, `IQueryBus`, `PipelineBehavior` |
| `Raindrop.CQRS.FluentValidation` | 命令/查询自动验证 | `ValidatorBehavior<T,R>`, `AddValidation()` |
| `Raindrop.AppConfiguration` | 强类型配置管理 | `IAppConfigurationManager<T>`, `AddAppConfiguration<T>()` |
| `Raindrop.AppConfiguration.Apollo` | Apollo 配置中心 | `AddApollo()` |
| `Raindrop.AppConfiguration.Database` | 数据库配置源 | `AddDbConfiguration()` |
| `Raindrop.AppConfiguration.Database.EFCore` | EF Core 配置源 | `AddEfConfiguration()` |
| `Raindrop.AspNetCore` | HTTP 统一响应/异常/日志 | `AddRaindropAspNetCore()`, `UseRaindropAspNetCore()` |
| `Raindrop.Observability.Abstractions` | 可观测性抽象 | `AccessLogEntry`, `IAccessLogWriter` |
| `Raindrop.Utility` | 通用工具 | `FriendlyException` |
| `MediatR` | 内嵌 MediatR 12.5.0 源码 | `IMediator`, `IRequest<T>`, `INotification` |

## 关键技术栈

- **运行时**: .NET 10.0
- **数据库**: PostgreSQL（通过 Npgsql + EF Core）
- **消息队列**: RabbitMQ
- **缓存**: Redis（可选，127.0.0.1:6379，无密码）
- **配置中心**: 携程 Apollo（可选）
- **MediatR**: 内嵌源码（非 NuGet 包），版本 12.5.0
- **验证**: FluentValidation
- **对象映射**: Mapster（可选）
- **日志**: NLog（通过 `ILogger` 抽象）

## 本地基础设施连接串

```
PostgreSQL: Host=localhost;Port=5432;Username=postgres;Password=123456;Database=postgres
RabbitMQ:   amqp://admin:admin123@localhost:15672
Redis:      127.0.0.1:6379（无密码）
```

## 编码规范（铁律）

1. **文档注释必须用中文！** 这是不可商量的铁律。
2. **写代码/改代码/删代码之前，必须先写文档与测试用例。** 遵循 TDD。
3. 遵循标准 C# 约定：
   - 四个空格缩进
   - 公共类型和成员用 PascalCase
   - 局部变量和参数用 camelCase
   - 异步方法以 `Async` 结尾
   - File-scoped namespace
   - Implicit usings
   - Nullable reference types 启用

```