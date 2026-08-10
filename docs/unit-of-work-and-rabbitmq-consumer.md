# 工作单元、幂等与 RabbitMQ 消费者设计说明

## 设计目标

RabbitMQ 消费者负责消费编排，不负责业务事务细节，也不负责幂等存储细节。

消费管道统一处理：

- 创建 DI Scope。
- 可选开启工作单元。
- 可选执行消费者幂等。
- 执行 `IEventHandler<TEvent>`。
- 工作单元提交成功后再 ack。
- 失败时 rollback，并进入 retry 或 dead-letter。

## 工作单元抽象

工作单元抽象独立放在：

```text
Raindrop.UnitOfWork.Abstractions
```

```csharp
public interface IUnitOfWork : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken = default);

    Task RollbackAsync(CancellationToken cancellationToken = default);
}

public interface IUnitOfWorkFactory
{
    ValueTask<IUnitOfWork> BeginAsync(CancellationToken cancellationToken = default);
}
```

`IUnitOfWorkFactory.BeginAsync()` 是真正的手动开启入口。它必须在返回前完成工作单元初始化；关系型实现需要在这里打开连接并开启数据库事务。`CommitAsync()` 只提交已经开启的工作单元，不再延迟到提交时才开启事务。

自动工作单元通过特性进入执行管道：

```csharp
[UnitOfWork]
public sealed class OrderCreatedHandler : IEventHandler<OrderCreatedEvent>
{
}
```

事件消费者执行器识别到 handler 类型上的 `[UnitOfWork]` 后，使用 `IUnitOfWorkFactory.BeginAsync()` 在 handler 执行前开启工作单元，handler 成功后提交，失败时回滚。没有标记 `[UnitOfWork]` 的 handler 不会因为容器里存在 `IUnitOfWorkFactory` 就自动开启事务。

## RabbitMQ 消费顺序

```text
收到 RabbitMQ 消息
  -> 反序列化事件
  -> 创建 DI Scope
  -> 解析 handler 上的幂等特性
  -> 如果需要幂等，先尝试 Begin 幂等 Key
  -> 如果 handler 需要工作单元，在执行前 Begin 工作单元
  -> 执行 IEventHandler<TEvent>
  -> Commit 工作单元
  -> MarkSucceeded 幂等 Key
  -> RabbitMQ ack
```

失败时：

```text
handler 失败或 commit 失败
  -> MarkFailed 幂等 Key
  -> rollback
  -> retry 或 dead-letter
```

关键规则：

- `Ack` 必须发生在 `Commit` 成功之后。
- `Commit` 失败时不能 `Ack`。
- `Handler` 失败时不能 `Commit`。
- 工作单元事务必须在 handler 执行前开启，不能延迟到 `CommitAsync()` 时才开启。
- handler 不应该自己控制消息确认，也不应该自己决定 RabbitMQ 重试或死信。

## 消费者幂等

幂等是消费侧能力，特性打在 handler 上，不打在事件类上。

同一个事件可能有多个消费者，每个消费者的幂等 Key 和过期策略都可能不同。

```csharp
[IdempotentConsumer(
    nameof(OrderCreatedEvent.OrderId),
    KeyPrefix = "order-created",
    ExpireSeconds = 86400)]
public sealed class OrderCreatedHandler : IEventHandler<OrderCreatedEvent>
{
}
```

多个字段：

```csharp
[IdempotentConsumer(
    "Customer.TenantId",
    nameof(OrderCreatedEvent.OrderId),
    KeyPrefix = "order-created")]
public sealed class OrderCreatedHandler : IEventHandler<OrderCreatedEvent>
{
}
```

生成的 Key：

```text
order-created:tenant-a:1001
```

`MessageId` 可以用于消息追踪和投递去重，但不作为业务幂等 Key 的默认设计。业务幂等 Key 应该由消费者从事件消息体中解析。

## 幂等抽象

```csharp
public interface IConsumerIdempotencyKeyResolver
{
    string Resolve<TEvent>(
        TEvent eventData,
        Type handlerType,
        IdempotentConsumerAttribute attribute)
        where TEvent : IEvent;
}
```

默认实现 `PropertyPathConsumerIdempotencyKeyResolver` 位于 `Raindrop.EventBus.Core`，支持：

- 普通属性：`OrderId`
- 嵌套属性：`Customer.TenantId`
- 简单集合索引：`Items[0].SkuId`

幂等存储抽象：

```csharp
public interface IConsumerIdempotencyStore
{
    Task<ConsumerIdempotencyBeginResult> TryBeginAsync(
        string key,
        TimeSpan expiration,
        CancellationToken cancellationToken = default);

    Task MarkSucceededAsync(string key, CancellationToken cancellationToken = default);

    Task MarkFailedAsync(string key, CancellationToken cancellationToken = default);
}
```

第一版在 `Raindrop.EventBus.Core` 提供 `InMemoryConsumerIdempotencyStore`，用于测试、开发和单实例场景。后续 Redis、数据库实现只需要实现同一个接口。

## 幂等状态

```text
Started
  -> 当前消费者获得处理权，可以执行 handler

Succeeded
  -> 已经处理成功，直接 ack

Processing
  -> 正在处理中，按 ConflictBehavior 处理
```

冲突策略：

```csharp
public enum IdempotentConflictBehavior
{
    Ack = 0,
    Retry = 1,
    Throw = 2
}
```

默认策略是 `Retry`，避免并发重复消息被静默吞掉。

## 与 CAP / Outbox 的关系

生产方 Outbox 或 CAP 后续也应该依赖工作单元抽象：

```text
业务方法
  -> 开启工作单元
  -> 写业务表
  -> 写 Outbox / CAP 本地消息
  -> 提交工作单元
```

RabbitMQ 包不直接实现 CAP，也不直接维护本地消息表。
