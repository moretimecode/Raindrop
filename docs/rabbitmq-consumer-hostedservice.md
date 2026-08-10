# RabbitMQ Consumer HostedService 设计

## 架构概览

当前 RabbitMQ 事件总线已经包含生产侧和消费侧的基础闭环：

```text
App
  -> IEventBus.PublishAsync(event)
  -> RoutingEventBus
  -> RabbitMqEventRouteResolver
  -> RabbitMqEventTransport
  -> RabbitMQ Broker
  -> RabbitMqHostedService
  -> RabbitMqConsumerExecutor
  -> IEventHandler<TEvent>
```

`RabbitMqEventTransport` 负责发布消息，`RabbitMqHostedService` 负责启动消费者、声明拓扑、反序列化消息、聚合 handler 执行结果，并统一决定 ACK、重试或死信。

## 路由规则

事件是否进入 RabbitMQ 由事件类型上的 `[RabbitMqEvent]` 决定：

```csharp
[RabbitMqEvent("order", "order.created")]
public sealed record OrderCreatedEvent(Guid OrderId) : IEvent;
```

`RabbitMqEventRouteResolver` 只解析 `[RabbitMqEvent]`，不会给无特性的事件补默认 RabbitMQ 路由。无特性事件如果注册了 MediatR 本地事件总线，会由 `MediatREventRouteResolver` 补成本地事件。

同一个事件如果既要走 RabbitMQ，又要走本地 MediatR，需要同时标记：

```csharp
[LocalEvent]
[RabbitMqEvent("order", "order.created")]
public sealed record OrderCreatedEvent(Guid OrderId) : IEvent;
```

## 消息元数据

发布到 RabbitMQ 时，`RabbitMqEventTransport` 会设置基础属性：

```text
Persistent = true
ContentType = application/json
Type = typeof(TEvent).AssemblyQualifiedName
Headers = {}
```

其中 `BasicProperties.Type` 是 HostedService 识别事件类型的关键字段。消费端会把消息属性里的 `Type` 与当前队列分组内的事件类型做匹配，然后按匹配到的类型反序列化消息体。

如果外部系统或测试辅助代码绕过 `RabbitMqEventTransport` 直接发消息，也必须设置 `properties.Type`，否则 HostedService 会拒绝该消息。

## 拓扑结构

每个 `(exchange, routingKey, queue)` 分组会声明一套主队列和重试队列。队列名默认使用：

```text
raindrop:{exchange}:{routingKey}
```

也可以通过 `RabbitMqEventAttribute.Queue` 显式指定。

主拓扑：

```text
Direct Exchange: {exchange}
  -> Queue: {queue}
       x-dead-letter-exchange: raindrop.dlx
       x-dead-letter-routing-key: raindrop.dlx
```

重试队列：

```text
Queue: {queue}:retry
  x-dead-letter-exchange: {exchange}
  x-dead-letter-routing-key: {routingKey}
```

全局死信拓扑：

```text
Direct Exchange: raindrop.dlx
  -> Queue: raindrop.dlq
```

当前死信目标是全局固定的 `raindrop.dlx` / `raindrop.dlq`，不是每个 handler 独立配置。

## 消费流程

HostedService 启动时会：

1. 扫描传入程序集里的 `IEventHandler<TEvent>`。
2. 找出事件类型上带 `[RabbitMqEvent]` 的 handler。
3. 按 `(exchange, routingKey, queue)` 分组。
4. 为每组声明 exchange、主队列、重试队列、全局死信队列和 binding。
5. 每组创建一个 consumer channel。
6. 设置 `BasicQos(0, 1, false)`。
7. 使用 `autoAck: false` 启动 `AsyncEventingBasicConsumer`。

收到消息后：

```text
读取 BasicProperties.Type
  -> 匹配事件类型
  -> 反序列化 body
  -> 读取 x-raindrop-failed-count
  -> 创建 RabbitMqReceivedMessage
  -> 逐个执行该事件类型的 handler
  -> 聚合所有 handler 结果
  -> 统一 ACK / Retry / DeadLetter
```

handler 不直接控制 RabbitMQ 消息确认。ACK、重试和死信都由 HostedService 聚合之后统一执行。

## Handler 执行

`RabbitMqConsumerExecutor` 每次执行单个 handler 时会创建独立 DI scope，并按顺序处理：

```text
创建 scope
  -> 解析 THandler
  -> 如果存在 [IdempotentConsumer]，先尝试占用幂等 Key
  -> 如果 handler 标记 [UnitOfWork]，则通过 IUnitOfWorkFactory 开启工作单元
  -> 执行 handler.HandleAsync
  -> 成功后提交工作单元
  -> 标记幂等 Key 成功
  -> 返回 Succeeded
```

失败时：

```text
handler 或 commit 抛异常
  -> 尝试 rollback
  -> 尝试 MarkFailed 幂等 Key
  -> 返回 Retry
```

`RollbackAsync` 和 `MarkFailedAsync` 的异常会被吞掉，避免因为补偿动作失败导致 RabbitMQ 消息悬挂为 unacked。

自动工作单元不是由 `IUnitOfWorkFactory` 是否存在来触发，而是由 handler 类型上的 `[UnitOfWork]` 显式触发。未标记 `[UnitOfWork]` 的 handler 即使容器里注册了 `IUnitOfWorkFactory`，也不会自动开启事务。

`RabbitMqHostedService` 只负责编排消息确认、重试和死信，不为了解析执行器额外创建 scope。单个 handler 的执行 scope 统一由 `RabbitMqConsumerExecutor` 创建，避免 HostedService 外层 scope 和 Executor 内层 scope 叠加。

## 幂等消费

幂等特性打在 handler 上：

```csharp
[IdempotentConsumer(
    nameof(OrderCreatedEvent.OrderId),
    KeyPrefix = "order-created",
    ExpireSeconds = 86400,
    ConflictBehavior = IdempotentConflictBehavior.Retry)]
public sealed class OrderCreatedHandler : IEventHandler<OrderCreatedEvent>
{
}
```

默认 key 解析器是 `PropertyPathConsumerIdempotencyKeyResolver`，支持普通属性、嵌套属性和简单集合索引。

默认内存实现是 `InMemoryConsumerIdempotencyStore`，适合测试、开发和单实例场景。生产环境如果需要跨进程幂等，应该替换成 Redis 或数据库实现。

## 重试与死信

handler 抛异常、工作单元提交失败、handler 解析失败等情况都会返回 `Retry`。HostedService 根据失败 handler 的重试策略决定下一步：

```text
未达到最大重试次数
  -> 发布原消息到 {queue}:retry
  -> 设置 per-message TTL
  -> ACK 原消息
  -> TTL 到期后由 retry queue 的 DLX 投回主 exchange

达到最大重试次数
  -> BasicReject(requeue: false)
  -> 由主队列 DLX 投递到 raindrop.dlq
```

失败次数保存在 header：

```text
x-raindrop-failed-count
```

每次重试都会递增。重试延迟使用线性递增：

```text
InitialDelay + DelayIncrement * (failedCount - 1)
```

并受 `MaxDelay` 限制。

## 重试策略配置

全局默认策略通过 `AddRabbitMqDeadLetterRetry` 或 `AddRabbitMqEventBus` 间接注册。

handler 可以通过 `[RabbitMqDeadLetterRetry]` 覆盖：

```csharp
[RabbitMqDeadLetterRetry(
    MaxRetryCount = 5,
    InitialDelaySeconds = 2,
    DelayIncrementSeconds = 2,
    MaxDelaySeconds = 300)]
public sealed class OrderCreatedHandler : IEventHandler<OrderCreatedEvent>
{
}
```

当前特性只支持重试次数和重试延迟，不支持自定义死信 exchange / routing key。死信统一进入全局 `raindrop.dlx` / `raindrop.dlq`。

## 异常路径

| 场景 | 当前行为 |
|------|----------|
| 消息缺少 `BasicProperties.Type` | `BasicReject(requeue: false)` |
| `Type` 无法匹配当前队列的事件类型 | `BasicReject(requeue: false)` |
| 反序列化失败 | `BasicReject(requeue: false)` |
| 没有匹配 handler | `BasicReject(requeue: false)` |
| 幂等已成功 | 返回 `AlreadyProcessed`，最终 ACK |
| 幂等冲突且策略为 `Ack` | 返回 `AlreadyProcessed`，最终 ACK |
| 幂等冲突且策略为 `Retry` | 进入重试或死信流程 |
| handler 抛异常 | 进入重试或死信流程 |
| handler 解析或反射调用失败 | 进入重试或死信流程 |
| retry 发布成功 | ACK 原消息 |
| retry 发布失败 | 不 ACK 原消息，由 RabbitMQ 重新投递 |

## DI 注册

只使用 RabbitMQ：

```csharp
services.AddRabbitMqEventBus(options =>
{
    options.ConnectionString = "amqp://admin:admin123@localhost:5672";
}, typeof(Program).Assembly);
```

RabbitMQ 与本地 MediatR 事件共存：

```csharp
services.AddMediatRLocalEventBus(typeof(Program).Assembly);
services.AddRabbitMqEventBus(options =>
{
    options.ConnectionString = "amqp://admin:admin123@localhost:5672";
}, typeof(Program).Assembly);
```

内部注册链：

```text
AddRabbitMqEventBus()
  -> AddRaindropEventHandlers(assemblies)
  -> AddEventBusCore<RabbitMqEventRouteResolver>()
  -> TryAddSingleton<RabbitMqEventBusOptions>
  -> TryAddSingleton<IConnection>
  -> TryAddEnumerable<IEventTransport, RabbitMqEventTransport>
  -> AddRabbitMqDeadLetterRetry()
  -> TryAddSingleton<IRabbitMqConnection, RabbitMqConnectionAdapter>
  -> TryAddSingleton<IRabbitMqTopologyDeclarer, RabbitMqTopologyDeclarer>
  -> TryAddSingleton<RabbitMqHostedService>
  -> TryAddEnumerable<IHostedService, RabbitMqHostedService>
```

`AddRabbitMqDeadLetterRetry()` 会注册：

```text
IConsumerIdempotencyKeyResolver -> PropertyPathConsumerIdempotencyKeyResolver
IRabbitMqDeadLetterRetryPolicyResolver -> RabbitMqDeadLetterRetryPolicyResolver
IRabbitMqConsumerExecutor -> RabbitMqConsumerExecutor
```

幂等存储默认没有在 RabbitMQ 包里强行注册。只有使用 `[IdempotentConsumer]` 时，应用需要注册 `IConsumerIdempotencyStore`，例如测试或单实例场景可使用 `AddInMemoryConsumerIdempotency()`。

## 配置选项

当前 `RabbitMqEventBusOptions` 只有一个配置项：

| 选项 | 默认值 | 说明 |
|------|--------|------|
| `ConnectionString` | `amqp://guest:guest@localhost:5672` | RabbitMQ 连接字符串 |

当前 consumer 行为固定为：

| 行为 | 当前值 |
|------|--------|
| `BasicQos` | `prefetchCount = 1` |
| `autoAck` | `false` |
| 死信 exchange | `raindrop.dlx` |
| 死信 queue | `raindrop.dlq` |

## 与 MediatR 共存

MediatR 本地事件总线没有 `IHostedService`。本地事件通过 `MediatRLocalEventTransport` 调用 `IPublisher` 在进程内发布。

```text
IEventBus.PublishAsync(event)
  |
  +-- MediatREventRouteResolver -> [LocalEvent] / 无特性
  |     -> MediatRLocalEventTransport
  |     -> MediatR IPublisher
  |
  +-- RabbitMqEventRouteResolver -> [RabbitMqEvent]
        -> RabbitMqEventTransport
        -> RabbitMQ Broker
        -> RabbitMqHostedService
        -> RabbitMqConsumerExecutor
```

多个 route resolver 是增量叠加的，所以 RabbitMQ 和 MediatR 可以同时存在。

## 当前未覆盖

- Publisher Confirms：生产侧目前是 fire-and-forget。
- 连接恢复编排：当前没有框架级断线重连和重新声明拓扑流程。
- 消息信封：还没有统一的 `MessageId`、时间戳、链路追踪元数据模型。
- 死信查看与重放工具：目前只投递到全局 DLQ。
- 生产方 Outbox / CAP：当前 RabbitMQ transport 不写本地消息表。
- 跨进程幂等存储：当前只提供内存实现，Redis / 数据库实现后续补。
