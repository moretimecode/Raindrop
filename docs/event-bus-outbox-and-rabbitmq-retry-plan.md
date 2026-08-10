# 事件总线 Outbox 与 RabbitMQ 重试预案

## 当前状态

RabbitMQ 事件总线当前已经实现：

- `[RabbitMqEvent]` 路由元数据。
- `RabbitMqEventRouteResolver`。
- `RabbitMqEventTransport` 发布消息。
- RabbitMQ 连接注册。
- exchange、queue、binding 自动声明。
- `RabbitMqHostedService` 后台消费者。
- per-message TTL 重试队列。
- 全局死信 exchange / queue。
- `[RabbitMqDeadLetterRetry]` handler 级重试策略。
- `IRabbitMqDeadLetterRetryPolicyResolver` 策略解析。
- `RabbitMqConsumerExecutor` 消费执行器。
- 消费侧幂等抽象和内存实现。
- 工作单元抽象接入点。
- RabbitMQ 集成测试。

这份文档不再描述“本次先做 RabbitMQ 死信重试”的阶段边界，而是沉淀后续 Outbox / CAP / 消息信封的设计方向。

## 设计目标

事件总线需要同时支持进程内事件、RabbitMQ、Kafka、BufferQueue 等多种投递通道。生产方一致性和消费方失败隔离要分开处理：

- 生产方一致性：后续通过 Outbox 或 CAP 解决。
- 消费方失败隔离：当前 RabbitMQ 包已经通过重试队列和死信队列解决基础问题。

`IEvent` 继续保持干净，不内置 `MessageId`、发生时间、链路追踪字段。消息进入中间件事件总线后，再由消息信封或中间件适配器生成消息元数据。

## Outbox 与 CAP 边界

本地消息表不应该属于 RabbitMQ、Kafka 或 BufferQueue 的私有实现。它属于当前服务的业务事务边界，用来保证“业务数据提交”和“消息待投递记录”在同一个数据库事务里提交。

后续如果接入 CAP，建议新增独立程序集：

```text
Raindrop.EventBus.Cap
```

或者：

```text
Raindrop.EventBus.Outbox.Cap
```

CAP 适配器负责：

- 在业务事务内记录待投递消息。
- 根据事件路由生成一个或多个投递目标。
- 后台把消息投递到 RabbitMQ、Kafka、BufferQueue 等中间件。

`RabbitMqEventTransport` 不应该直接写本地消息表。否则以后接 CAP 时，RabbitMQ、Kafka、BufferQueue 都会各自出现一套生产方事务逻辑，边界会变重。

## 本地消息表建议模型

如果未来不直接使用 CAP，而是自研 Outbox，也建议使用一套统一本地消息表，而不是 RabbitMQ、Kafka、BufferQueue 各自一套表。

```text
OutboxMessages
- MessageId
- EventType
- Payload
- Headers
- CreatedAt

OutboxDeliveries
- DeliveryId
- MessageId
- TransportName
- Destination
- Status
- RetryCount
- NextRetryAt
- LastError
```

一条事件只保存一次消息内容。如果它要投递到多个中间件，就生成多条投递记录。

例如同一个事件同时进入 RabbitMQ 和 Kafka：

```text
OutboxMessages
  message-1001, OrderCreatedEvent, payload

OutboxDeliveries
  delivery-1, message-1001, RabbitMQ, order/order.created
  delivery-2, message-1001, Kafka, order-created
```

这样可以保证事件内容只有一份，投递状态按目标分别维护。

## MessageId 策略

`MessageId` 不属于领域事件本身，应该在事件进入中间件事件总线时生成。

建议后续抽象：

```csharp
public interface IMessageIdGenerator
{
    long NextId();
}
```

默认实现可以使用 Snowflake。相比随机 `Guid`，递增或趋势递增 ID 更适合数据库索引写入，也方便本地消息表、投递记录和日志排查。

当前 RabbitMQ 发布侧还没有统一消息信封，事件类型暂时通过 `BasicProperties.Type = typeof(TEvent).AssemblyQualifiedName` 写入 RabbitMQ 属性。

## 消息信封预案

后续建议在事件总线层引入消息信封，但不要污染 `IEvent`：

```text
EventEnvelope
- MessageId
- EventType
- Payload
- Headers
- CreatedAt
- TraceId
- CorrelationId
```

生产侧流程：

```text
IEvent
  -> RoutingEventBus
  -> EventEnvelopeFactory
  -> Outbox / Transport
```

消费侧流程：

```text
Broker Message
  -> EventEnvelope
  -> Deserialize Payload
  -> IEventHandler<TEvent>
```

消息信封应该属于事件总线基础设施，不属于 `Raindrop.Data`。

## RabbitMQ 重试策略现状

RabbitMQ 的死信重试属于消费方策略，当前放在 handler 上：

```csharp
[RabbitMqDeadLetterRetry(
    MaxRetryCount = 5,
    InitialDelaySeconds = 2,
    DelayIncrementSeconds = 2,
    MaxDelaySeconds = 300)]
public sealed class OrderCreatedHandler : IEventHandler<OrderCreatedEvent>
{
    public Task HandleAsync(OrderCreatedEvent eventData, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
```

当前支持的策略字段：

- `MaxRetryCount`：失败达到该次数后进入死信，不再重试。
- `InitialDelaySeconds`：第一次失败后的重试延迟。
- `DelayIncrementSeconds`：每次失败后增加的重试延迟。
- `MaxDelaySeconds`：重试延迟上限。

当前不支持在 handler 上自定义 `DeadLetterExchange` / `DeadLetterRoutingKey`。死信统一进入全局 `raindrop.dlx` / `raindrop.dlq`。

## 与工作单元的关系

消费侧通过 `[UnitOfWork]` 和可选的 `IUnitOfWorkFactory` 接入工作单元：

```text
收到 RabbitMQ 消息
  -> 创建 DI scope
  -> 幂等 Begin
  -> 如果 handler 标记 [UnitOfWork]，则 Begin UnitOfWork
  -> 执行 handler
  -> 如果开启了 UnitOfWork，则 Commit UnitOfWork
  -> MarkSucceeded
  -> ACK
```

生产方 Outbox / CAP 后续也应该依赖同一套工作单元抽象：

```text
业务方法
  -> Begin UnitOfWork
  -> 写业务表
  -> 写 Outbox / CAP 本地消息
  -> Commit UnitOfWork
```

这样 RabbitMQ 包不需要知道业务数据库事务细节，Outbox / CAP 也不会绑死某个消息中间件。

这里的自动工作单元必须由 handler 上的 `[UnitOfWork]` 明确触发，不能仅因为容器中存在 `IUnitOfWorkFactory` 就自动包一层事务。这样可以让普通消费者保持无事务执行，也为后续 HTTP Filter / Middleware 使用同一套特性语义留出一致入口。

## 后续待补

- 统一消息信封。
- `IMessageIdGenerator` 默认实现。
- Outbox / CAP 适配包。
- Outbox 投递状态模型。
- 死信消息查询与重放工具。
- RabbitMQ Publisher Confirms。
- RabbitMQ 连接恢复与重新声明拓扑流程。
- Redis 或数据库版消费幂等存储。
