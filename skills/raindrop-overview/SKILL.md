---
name: raindrop-overview
description: Raindrop .NET 微服务框架总览。当用户想做 DDD、CQRS、事件驱动、配置管理相关的 .NET 微服务，或询问 Raindrop 框架有什么能力时使用。先读此技能了解全局，再按需加载具体子技能。
---

# Raindrop — .NET 微服务框架骨架

Raindrop 是一个轻量级 .NET 微服务框架，提供 6 大能力域、17 个 NuGet 包。所有包以 `Raindrop.*` 命名，版本在 `Directory.Build.props` 中统一管理。

## 能力地图

| 子技能 | NuGet 包 | 一句话 |
|--------|----------|--------|
| [Raindrop.Data](../raindrop-data/SKILL.md) | `Raindrop.Data` + `.EFCore` | Entity、AggregateRoot、IEvent、IRepository、规约、分页 |
| [Raindrop.UoW](../raindrop-unit-of-work/SKILL.md) | `Raindrop.UnitOfWork.Abstractions` | IUnitOfWork、递归领域事件、[UnitOfWork] 特性 |
| [Raindrop.CQRS](../raindrop-cqrs/SKILL.md) | `Raindrop.CQRS` + `.FluentValidation` | ICommand/IQuery、Bus、Pipeline Behavior、自动校验 |
| [Raindrop.EventBus](../raindrop-event-bus/SKILL.md) | `Raindrop.EventBus.*` | IEventBus、本地事件、RabbitMQ、死信重试、幂等消费 |
| [Raindrop.Config](../raindrop-app-configuration/SKILL.md) | `Raindrop.AppConfiguration.*` | 强类型配置、递归验证、热重载、Apollo/数据库 |
| [Raindrop.AspNetCore](../raindrop-aspnetcore/SKILL.md) | `Raindrop.AspNetCore` | 统一响应、全局异常、Access Log |
| [Raindrop.Utility](../raindrop-utility/SKILL.md) | `Raindrop.Utility` | FriendlyException、扩展方法 |

## 包依赖图

```
Raindrop.Utility
  └── FriendlyException, Extensions

Raindrop.Data
  └── IEvent, Entity<T>, AggregateRoot<T>, IRepository<T>, ISpecification<T>

Raindrop.UnitOfWork.Abstractions
  └── IUnitOfWork, IUnitOfWorkFactory, [UnitOfWork]

Raindrop.Data.EFCore
  ├── Raindrop.Data
  ├── Raindrop.UnitOfWork.Abstractions
  └── EF Core Repository + UnitOfWork 实现

Raindrop.CQRS
  ├── Raindrop.Utility
  ├── MediatR (内嵌 12.5.0)
  └── ICommand/IQuery, Bus, Pipeline Behavior

Raindrop.CQRS.FluentValidation
  ├── Raindrop.CQRS
  └── ValidatorBehavior, 自动校验

Raindrop.EventBus.Abstractions
  ├── Raindrop.Data (IEvent)
  └── IEventBus, IEventHandler, IEventRouteResolver, IEventTransport

Raindrop.EventBus.Core
  ├── Raindrop.EventBus.Abstractions
  └── RoutingEventBus, InMemoryIdempotencyStore

Raindrop.EventBus.MediatR
  ├── Raindrop.EventBus.Core
  └── MediatR 本地事件传输

Raindrop.EventBus.RabbitMQ
  ├── Raindrop.EventBus.Core
  └── RabbitMQ HostedService, DLX 重试, [RabbitMqEvent]

Raindrop.AppConfiguration
  ├── Raindrop.AppConfiguration.Abstractions
  └── DataAnnotations 递归验证, 热重载

Raindrop.AspNetCore
  ├── Raindrop.Observability.Abstractions
  └── 统一响应, Access Log
```

## 典型组合

| 场景 | 需要的包 |
|------|----------|
| 最小 Web API | `Raindrop.AspNetCore` + `Raindrop.Utility` |
| 标准 CQRS 服务 | 上述 + `Raindrop.CQRS` + `Raindrop.CQRS.FluentValidation` |
| DDD 微服务 | 上述 + `Raindrop.Data` + `Raindrop.Data.EFCore` |
| 事件驱动微服务 | 上述 + `Raindrop.EventBus.MediatR` + `Raindrop.EventBus.RabbitMQ` |
| 配置中心集成 | 上述 + `Raindrop.AppConfiguration` + `Raindrop.AppConfiguration.Apollo` |

## 使用说明

1. 根据需求从上表中找到对应的子技能
2. 读子技能了解具体 API、代码示例、DI 注册方式
3. 所有 DI 注册都是 `services.AddRaindrop*()` 扩展方法
4. 本地 NuGet 源 `raindrop-local`，路径 `nuget-local/`
