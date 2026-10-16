# 事件总线设计实现

## 职责与包边界

业务通过 `IEventBus` 发布事件，通过 `IEventHandler<TEvent>` 处理事件。总线合并路由 Resolver 的结果，委托具体 transport；EF Core、MediatR 和 RabbitMQ 的实现互相分离。

| 包 | 实现职责 |
|---|---|
| `Raindrop.Data` | `IEvent`、实体和聚合根，不依赖事件总线 |
| `Raindrop.EventBus.Abstractions` | 事件、路由、transport、Outbox 和消费幂等的稳定接缝 |
| `Raindrop.EventBus.Core` | `RoutingEventBus`、handler 扫描、幂等键解析和内存幂等存储 |
| `Raindrop.EventBus.MediatR` | 本地路由和进程内同步调用 |
| `Raindrop.EventBus.RabbitMQ` | RabbitMQ 发布、目标快照、消费宿主、重试与死信 |
| `Raindrop.Data.EFCore` | UoW 事件准备及可选的同事务 Outbox 登记 |

## 事件与业务 API

`IEvent` 位于 `Raindrop.Data.Events`；`IIntegrationEvent : IEvent` 位于总线抽象包。前者是基础标记，后者表达需要跨进程交付的集成事实。聚合可以通过 `AddDomainEvent` 保存任一种事件，提交行为由 [UoW](repository-and-unitofwork.md) 编排。

```csharp
public interface IEventBus
{
    Task PublishAsync<TEvent>(TEvent eventData, CancellationToken cancellationToken = default)
        where TEvent : IEvent;
}

public interface IEventHandler<in TEvent> where TEvent : IEvent
{
    Task HandleAsync(TEvent eventData, CancellationToken cancellationToken = default);
}
```

集成事件沿用原事件类型，不要求业务另建 Outbox 专用事件或调用消息表存储接口。

```csharp
public sealed record OrderPaid(Guid OrderId) : IEvent;

[RabbitMqEvent("orders", "order.paid")]
public sealed record OrderPaidIntegrationEvent(Guid OrderId) : IIntegrationEvent;
```

## 路由解析

路由元数据继承 `EventRouteAttribute`。同一事件允许多个路由特性，特性不从父类型继承。

| Resolver | 返回的路由 |
|---|---|
| `MediatREventRouteResolver` | 显式 `[LocalEvent]`；没有任何路由特性时补默认 Local |
| `RabbitMqEventRouteResolver` | 只解析 `[RabbitMqEvent(exchange, routingKey)]` |

`IIntegrationEvent` 标记本身不会生成 exchange、routing key 或 queue。Resolver 的结果增量合并，不是只选一个 Resolver。

| 情况 | 默认即时发布 | 启用 Outbox |
|---|---|---|
| 集成事件有明确远程路由 | 调用匹配 transport 即时发送 | 在活动 UoW 中登记，不即时发送 |
| 集成事件无特性，注册了本地 Resolver | 默认本地调用 | 拒绝本地回退，提交前失败 |
| 没有 Resolver 贡献任何路由 | 抛异常 | 抛异常并阻止当前登记事务提交 |
| 同一集成事件混合 Local 与远程路由 | 依次调用两类 transport | 明确拒绝 |
| 普通 `IEvent` 携带远程路由 | 按旧路由规则调用 transport | 拒绝，跨进程事件需要实现 `IIntegrationEvent` |

本地事件没有 handler 时，MediatR 可以正常完成，不因零 handler 报错。远程发布不要求发布进程注册消费 handler；远端队列、绑定和消费者是否存在属于 MQ 部署与消费拓扑，不能靠本地 handler 列表证明交付。

## RoutingEventBus 与 transport

默认模式遍历所有路由，并调用所有 `CanHandle` 返回 true 的 transport；没有匹配适配器时抛异常。启用 Outbox 后，集成事件优先交给 `IEventOutbox`，普通领域事件仍通过本地 transport 处理。

```csharp
public interface IEventTransport
{
    bool CanHandle(EventRoute route);

    EventDeliveryTarget CaptureTarget(EventRoute route)
        => throw new NotSupportedException("当前 transport 不支持 Outbox 发送目标快照。");

    Task PublishAsync<TEvent>(TEvent eventData, EventRoute route,
        CancellationToken cancellationToken = default) where TEvent : IEvent;
}

public sealed record EventDeliveryTarget(string Transport, string Version, string TargetJson);
```

`CaptureTarget` 只生成稳定、规范化的发送地址，不能连接 MQ 或发送消息。相同目标应生成相同快照，`Version` 是目标格式版本。旧 transport 不实现此方法时仍可即时发布，可靠登记则明确失败。

RabbitMQ 的目标格式为 transport `rabbitmq`、版本 `1`，JSON 保存 `exchange` 与 `routingKey`；queue 属于消费者，不进入生产地址快照。该能力只是保存地址，尚未提供读取快照进行后台发送的执行器。

## Outbox 接缝

```csharp
public interface IEventOutbox
{
    Task EnqueueAsync(IIntegrationEvent eventData,
        CancellationToken cancellationToken = default);
}
```

它是框架的登记接缝，业务继续调用 EventBus。EF 实现负责校验活动事务与实际 DbContext、稳定契约、全部发送目标及 payload，再加入当前 ChangeTracker；不独立 SaveChanges 或 Commit。

Outbox 模式每条远程路由必须且只能匹配一个 transport，所有目标在登记之前完成预检，不能部分成功。直接 Publish 返回表示意图被本次事务接受；不表示提交或 MQ 交付。[本地消息表与 Outbox](transactional-outbox.md)描述装配、持久模型和失败边界。

## 注册与 handler 扫描

```csharp
services.AddMediatRLocalEventBus(typeof(Program).Assembly);
services.AddRabbitMqEventBus(typeof(Program).Assembly);
```

两个实现可以共存。组件内部通过以下入口增量注册 Resolver，业务通常不需要自行调用：

```csharp
services.AddEventBusCore<MediatREventRouteResolver>();
services.AddEventBusCore<RabbitMqEventRouteResolver>();
```

handler 按传入程序集扫描，使用 `IEventHandler<TEvent>`；消费侧需要事务、幂等与重试配置时，详见 [RabbitMQ 消费组件](rabbitmq-consumer-hostedservice.md)。生产发布不使用消费端的队列命名或幂等键代替发送目标、消息身份。
