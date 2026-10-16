# RabbitMQ 发布与消费设计实现


## 架构概览

RabbitMQ 包提供即时发布和消费编排。下图是默认未启用 Outbox 的调用链：

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

启用 [Outbox](transactional-outbox.md) 后，集成发布只登记本地消息表，尚不执行后台发送。`RabbitMqEventTransport` 负责默认模式的即时发布，`RabbitMqHostedService` 负责启动消费者、声明拓扑、反序列化消息、聚合 handler 执行结果，并统一决定 ACK、重试或死信。

## 路由规则

事件是否进入 RabbitMQ 由事件类型上的 `[RabbitMqEvent]` 决定：

```csharp
[RabbitMqEvent("order", "order.created")]
public sealed record OrderCreatedEvent(Guid OrderId) : IIntegrationEvent;
```

`RabbitMqEventRouteResolver` 只解析 `[RabbitMqEvent]`，不会给无特性的事件补默认 RabbitMQ 路由。无特性事件如果注册了 MediatR 本地事件总线，会由 `MediatREventRouteResolver` 补成本地事件。

默认即时发布可以同时声明 RabbitMQ 和本地 MediatR 路由；Outbox 模式拒绝集成事件混合本地路由：

```csharp
[LocalEvent]
[RabbitMqEvent("order", "order.created")]
public sealed record OrderCreatedEvent(Guid OrderId) : IIntegrationEvent;
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

即时 transport 使用 `mandatory: false`，当前没有 Publisher Confirms；返回不证明 broker 接收或存在匹配队列。发布进程没有 handler 不会因此报错，消费宿主只为扫描到的 handler 声明拓扑，发布方法不会自动创建消费队列。

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
6. 异步设置 `BasicQosAsync(0, 1, false)`。
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

`RollbackAsync` 和 `MarkFailedAsync` 的异常会被执行器隔离，随后继续返回重试结果。是否成功转移/ACK 原消息仍由 HostedService 及 channel 的结果决定。

自动工作单元不是由 `IUnitOfWorkFactory` 是否存在来触发，而是由 handler 类型上的 `[UnitOfWork]` 显式触发。未标记 `[UnitOfWork]` 的 handler 即使容器里注册了 `IUnitOfWorkFactory`，也不会自动开启事务。当前即使标记了特性，缺少工厂时也仍会无事务执行；使用方必须完成装配，不能把特性本身当作事务保证。HTTP 自动 UoW 不由本组件提供。

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

默认内存实现是 `InMemoryConsumerIdempotencyStore`，适合测试、开发和单实例场景；框架尚未提供 Redis/数据库持久实现。仅替换存储也不会自动使消费成功标记参加业务事务。

幂等键按 handler 配置，支持多个属性路径，例如 `"Customer.TenantId"` 与 `nameof(OrderCreatedEvent.OrderId)`，可组合成 `order-created:tenant-a:1001`。当前 Resolver 读取消息体，没有统一信封上下文；消息去重与业务执行幂等需要分别考虑。

```csharp
public interface IConsumerIdempotencyKeyResolver
{
    string Resolve<TEvent>(TEvent eventData, Type handlerType,
        IdempotentConsumerAttribute attribute) where TEvent : IEvent;
}

public interface IConsumerIdempotencyStore
{
    Task<ConsumerIdempotencyBeginResult> TryBeginAsync(string key,
        TimeSpan expiration, CancellationToken cancellationToken = default);
    Task MarkSucceededAsync(string key, CancellationToken cancellationToken = default);
    Task MarkFailedAsync(string key, CancellationToken cancellationToken = default);
}
```

| TryBegin 结果 | 当前行为 |
|---|---|
| Started | 获得处理权，继续执行 handler |
| Succeeded | 返回 AlreadyProcessed，参与最终 ACK 决策 |
| Processing | 按 ConflictBehavior 处理：Ack、Retry 或 Throw；默认 Retry |

幂等成功是在业务 Commit 之后单独标记，尚未与业务形成原子提交。同队列多个 handler 分别执行并各自拥有 scope/UoW，不是一个共同事务；整条消息重试时，已经成功的 handler 可能再次被调用。业务仍需处理重复执行。

## 重试与死信

handler 抛异常、工作单元提交失败、handler 解析失败等情况都会返回 `Retry`。HostedService 根据失败 handler 的重试策略决定下一步：

```text
未达到最大重试次数
  -> 调用 BasicPublishAsync 将原 body 与复制的语义属性发布到 {queue}:retry
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

重试发布保留原 body、Type、已有 MessageId/CorrelationId 和 headers，并增加失败次数及消息 TTL。当前重试发布没有确认机制，调用返回后 ACK 不能证明已可靠移交。

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
| retry 发布调用返回 | ACK 原消息 |
| retry 发布调用抛异常 | 不执行该路径后续 ACK；连接/通道关闭后由 broker 重投未确认消息 |

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
  -> TryAddSingleton<IRabbitMqConnection, RabbitMqConnectionAdapter>
  -> TryAddEnumerable<IEventTransport, RabbitMqEventTransport>
  -> AddRabbitMqDeadLetterRetry()
  -> TryAddSingleton<IRabbitMqTopologyDeclarer, RabbitMqTopologyDeclarer>
  -> TryAddSingleton<RabbitMqHostedService>
  -> TryAddEnumerable<IHostedService, RabbitMqHostedService>
```

RabbitMQ.Client 7.x 使用异步连接和通道 API。适配器在首次创建通道时连接 Broker；DI 注册和解析不会等待网络连接。调用方可以预先注册 `IConnection`，适配器复用它并保留调用方的释放责任。默认注册不向 DI 暴露 `IConnection`；业务发布使用 `IEventBus.PublishAsync`。底层适配接口采用 `CreateChannelAsync`、`DeclareAsync` 等异步契约，并传递取消令牌。

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
| `BasicQosAsync` | `prefetchCount = 1` |
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

多个 route resolver 是增量叠加的，所以 RabbitMQ 和 MediatR 可以同时存在。上图同样描述默认即时发布；Outbox 模式的集成事件在进入 transport 发送前转入事务登记。

## 能力边界

| 能力 | 当前实现 |
|---|---|
| 正常事务消费 ACK | handler Commit、可选幂等成功标记之后，由宿主聚合结果 ACK |
| 业务与消费成功原子提交 | 未实现；MarkSucceeded 是 Commit 后的独立操作 |
| Commit Unknown | UoW 保留原身份；消费执行器尚未单独核实，仍按普通异常返回 Retry |
| 重试交接确认 | 未实现 Publisher Confirms/Return，不能证明重发后 ACK 的可靠交接 |
| 本地 Inbox 接管、扫描重试 | 未实现；当前重试依赖 MQ 队列，没有本地业务恢复 worker |
| 结果通知持久化与独立重试 | 未实现，不能用内存 OnCompleted 代替 |
| 生产 Outbox | EF 组件仅完成同事务登记，Rabbit transport 不写表，也尚无后台派发器 |
| 多实例持久幂等与业务执行幂等 | 内存实现不提供跨进程保证；框架不承诺 exactly-once |
| 断线恢复、在途排空与死信重放工具 | 未提供完整框架级恢复编排 |

当前没有“MQ 托管/本地持久化”两种恢复策略的可选 API。后续增加本地恢复时，同一订阅只能有一个业务重试所有者，不能同时启动 MQ 与本地扫描恢复，也不能失败后静默切换。死信队列是投递机制，不代表完整可靠重试策略已经实现。

需要事务幂等和结果闭环时，消费成功事实及通知意图应与业务一起保存，通知失败只重试通知，不重跑已提交业务；这些尚未实现，不属于当前 `[UnitOfWork]` 或重试特性的保证。
