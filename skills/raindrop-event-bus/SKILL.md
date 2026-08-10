---
name: raindrop-event-bus
description: Raindrop 事件总线。当用户需要进程内事件发布、RabbitMQ 跨服务消息、事件路由、消费重试与死信、幂等消费时使用。
---

# Raindrop.EventBus — 事件总线

## NuGet 包

```
Raindrop.EventBus.Abstractions    — 抽象契约
Raindrop.EventBus.Core            — 路由引擎 + 内存幂等
Raindrop.EventBus.MediatR         — 进程内 MediatR 事件
Raindrop.EventBus.RabbitMQ        — RabbitMQ 生产+消费+死信重试
```

## 发布事件 — 单一入口

```csharp
public interface IEventBus
{
    Task PublishAsync<TEvent>(TEvent eventData, CancellationToken ct = default)
        where TEvent : IEvent;
}
```

## 路由 — 增量叠加，非二选一

事件的路由由多个 `IEventRouteResolver` 决定：

```csharp
// 无特性的 IEvent → 自动补 LocalEvent → MediatR 本地处理
// [RabbitMqEvent] → RabbitMQ
// 两者可以共存：
[LocalEvent]
[RabbitMqEvent("order", "order.created")]
public sealed record OrderCreatedEvent(Guid OrderId) : IIntegrationEvent;
```

## DI 注册

```csharp
// 仅本地事件
services.AddMediatRLocalEventBus(typeof(Program).Assembly);

// 仅 RabbitMQ
services.AddRabbitMqEventBus(options =>
{
    options.ConnectionString = "amqp://admin:admin123@localhost:5672";
}, typeof(Program).Assembly);

// 两者共存
services.AddMediatRLocalEventBus(typeof(Program).Assembly);
services.AddRabbitMqEventBus(options => { ... }, typeof(Program).Assembly);
```

## 本地事件（MediatR）

Handler 与标准 `IEventHandler<TEvent>` 一致，通过 `IEventBus` 发布。

## RabbitMQ 事件

### 事件声明

```csharp
[RabbitMqEvent("order", "order.created")]
public sealed record OrderCreatedEvent(Guid OrderId) : IIntegrationEvent;
```

参数：`(exchange, routingKey, queue?)`，queue 默认 `raindrop:{exchange}:{routingKey}`。

### 消费处理器

```csharp
public sealed class OrderCreatedHandler : IEventHandler<OrderCreatedEvent>
{
    public async Task HandleAsync(OrderCreatedEvent eventData, CancellationToken ct) { ... }
}
```

### 拓扑结构

```
Exchange: {exchange}
  → Queue: {queue}  (DLX → raindrop.dlx)
  → Queue: {queue}:retry  (DLX → {exchange}, per-message TTL)
Exchange: raindrop.dlx
  → Queue: raindrop.dlq  (全局死信)
```

### 消费流程

```
收到消息 → 匹配事件类型 → 反序列化 → 逐个执行 handler
  → 聚合结果 → 统一 ACK / Retry / DeadLetter
```

handler 不直接控制 ACK，由 HostedService 聚合后统一决定。

## 重试与死信

### 全局默认策略

`AddRabbitMqEventBus` 自动注册：最多 3 次重试，初始 2s，增量 2s，最大 300s。

### Handler 级覆盖

```csharp
[RabbitMqDeadLetterRetry(MaxRetryCount = 5, InitialDelaySeconds = 2,
    DelayIncrementSeconds = 2, MaxDelaySeconds = 300)]
public sealed class OrderCreatedHandler : IEventHandler<OrderCreatedEvent> { }
```

流程：未达最大 → `{queue}:retry` + TTL → 回主队列 | 达到最大 → DLQ。

## 幂等消费

```csharp
[IdempotentConsumer(
    nameof(OrderCreatedEvent.OrderId),
    KeyPrefix = "order-created",
    ExpireSeconds = 86400,
    ConflictBehavior = IdempotentConflictBehavior.Retry)]  // Retry 或 Ack
public sealed class OrderCreatedHandler : IEventHandler<OrderCreatedEvent> { }
```

- 默认 key 解析：`PropertyPathConsumerIdempotencyKeyResolver`（支持嵌套属性）
- 默认存储：`InMemoryConsumerIdempotencyStore`（生产需换 Redis/DB）

## 工作单元集成

```csharp
[UnitOfWork]
public sealed class OrderCreatedHandler : IEventHandler<OrderCreatedEvent>
{
    public async Task HandleAsync(OrderCreatedEvent e, CancellationToken ct)
    {
        var order = await _repo.GetAsync(e.OrderId);
        order.Complete();
        await _repo.UnitOfWork.CommitAsync(ct);
        // Executor 自动开启/提交/回滚 UoW
    }
}
```

## 异常处理速查

| 场景 | 行为 |
|------|------|
| 缺少 Type 头 | `BasicReject(requeue:false)` |
| Type 无法匹配 | `BasicReject(requeue:false)` |
| 反序列化失败 | `BasicReject(requeue:false)` |
| 幂等已成功 | ACK |
| handler 异常 / commit 失败 | 重试或死信 |
| retry 发布失败 | 不 ACK，RabbitMQ 重新投递 |

## 当前限制

- Publisher Confirms 未实现（fire-and-forget）
- Outbox/CAP 本地消息表未实现
- 消息信封模型（MessageId、TraceContext）未实现
- 跨进程幂等存储需自行实现
