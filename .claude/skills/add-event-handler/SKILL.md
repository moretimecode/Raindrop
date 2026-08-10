---
name: add-event-handler
description: 在 Raindrop 项目中添加事件处理器（EventHandler），配置事件路由（本地/消息队列）、幂等消费和死信重试策略。当用户需要添加事件监听、消息消费、领域事件处理时使用。触发词：事件处理、EventHandler、消息消费、发布事件、订阅事件、RabbitMQ 消息、领域事件处理。
---

# 添加事件处理器（EventHandler）

在 Raindrop 项目中添加事件处理器，包括定义事件、编写 Handler、配置路由和消费策略。

## 前置知识

参考文档：
- [docs/event-bus-abstractions.md](docs/event-bus-abstractions.md) — 事件总线抽象设计
- [docs/event-bus-outbox-and-rabbitmq-retry-plan.md](docs/event-bus-outbox-and-rabbitmq-retry-plan.md) — Outbox 与重试预案
- [docs/unit-of-work-and-rabbitmq-consumer.md](docs/unit-of-work-and-rabbitmq-consumer.md) — 工作单元与 RabbitMQ 消费者
- [docs/rabbitmq-consumer-hostedservice.md](docs/rabbitmq-consumer-hostedservice.md) — RabbitMQ HostedService 设计
- [project-overview](.claude/skills/raindrop-project-overview/SKILL.md) — 项目架构总览

## 步骤

### 1. 定义事件

事件必须实现 `IEvent`。如果是跨服务事件，额外标记 `IIntegrationEvent`：

```csharp
using Raindrop.Data.Events;

// 可选：标记为集成事件（跨服务）
public sealed record OrderPaidEvent(Guid OrderId, decimal Amount) : IIntegrationEvent;
```

### 2. 配置事件路由

事件发布到哪个通道由 Attribute 决定。可以同时配置多个路由：

```csharp
// 只发布到本地 MediatR
[LocalEvent]
public sealed record UserCreatedEvent(Guid UserId) : IEvent;

// 只发布到 RabbitMQ
[RabbitMqEvent("order", "order.created")]
public sealed record OrderCreatedEvent(Guid OrderId) : IIntegrationEvent;

// 同时发布到本地和 RabbitMQ
[LocalEvent]
[RabbitMqEvent("order", "order.paid")]
public sealed record OrderPaidEvent(Guid OrderId) : IIntegrationEvent;
```

- `[LocalEvent]`：在进程内通过 MediatR 发布
- `[RabbitMqEvent(exchange, routingKey)]`：发布到 RabbitMQ
- 无标记的事件：如果注册了 MediatR 本地总线，会自动补成 `[LocalEvent]`

### 3. 实现事件处理器

```csharp
using Raindrop.EventBus.Abstractions;
using Raindrop.Data.Entities;
using Raindrop.Data.Events;

public sealed class OrderPaidEventHandler : IEventHandler<OrderPaidEvent>
{
    private readonly ILogger<OrderPaidEventHandler> _logger;
    private readonly IRepository<Invoice, Guid> _invoiceRepo;

    public OrderPaidEventHandler(
        ILogger<OrderPaidEventHandler> logger,
        IRepository<Invoice, Guid> invoiceRepo)
    {
        _logger = logger;
        _invoiceRepo = invoiceRepo;
    }

    public async Task HandleAsync(
        OrderPaidEvent eventData,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("处理订单支付事件: {OrderId}", eventData.OrderId);

        // 业务逻辑
        var invoice = new Invoice(eventData.OrderId, eventData.Amount);
        await _invoiceRepo.InsertAsync(invoice);
    }
}
```

**关键规则**：Handler 不要自己提交事务或确认消息。事务由 `[UnitOfWork]` 特性控制，消息确认由框架自动处理。

### 4. 配置幂等消费（可选）

当需要防止重复消费时，在 Handler 上标记：

```csharp
[IdempotentConsumer(
    nameof(OrderPaidEvent.OrderId),  // 幂等键字段
    KeyPrefix = "order-paid",         // 键前缀
    ExpireSeconds = 86400,            // 过期时间
    ConflictBehavior = IdempotentConflictBehavior.Ack)]  // 冲突策略
public sealed class OrderPaidEventHandler : IEventHandler<OrderPaidEvent>
{
    // ...
}
```

多字段组合幂等键：

```csharp
[IdempotentConsumer(
    "Customer.TenantId",
    nameof(OrderCreatedEvent.OrderId),
    KeyPrefix = "order-created")]
```

幂等冲突策略：

| 策略 | 说明 |
|------|------|
| `Ack` | 直接 ACK，视为已处理 |
| `Retry`（默认） | 重新入队等待重试 |
| `Throw` | 抛出异常 |

需要注册幂等存储：

```csharp
// 开发/测试/单实例：内存实现
services.AddInMemoryConsumerIdempotency();
```

### 5. 配置死信重试策略（可选，仅 RabbitMQ）

```csharp
[RabbitMqDeadLetterRetry(
    MaxRetryCount = 5,
    InitialDelaySeconds = 2,
    DelayIncrementSeconds = 2,
    MaxDelaySeconds = 300)]
public sealed class OrderPaidEventHandler : IEventHandler<OrderPaidEvent>
{
    // ...
}
```

重试延迟计算：`InitialDelay + DelayIncrement * (failedCount - 1)`，不超过 `MaxDelay`。

### 6. 配置工作单元（可选）

如果 Handler 需要操作数据库，标记 `[UnitOfWork]` 自动开启事务：

```csharp
[UnitOfWork]
public sealed class OrderPaidEventHandler : IEventHandler<OrderPaidEvent>
{
    public async Task HandleAsync(OrderPaidEvent eventData, CancellationToken cancellationToken)
    {
        // 操作多个 Repository
        // 框架自动在进入前 Begin UnitOfWork
        // 成功后 Commit，失败后 Rollback
    }
}
```

消费流程：

```text
收到消息 → 创建 DI Scope → 幂等 Begin → [UnitOfWork] Begin
  → 执行 HandleAsync → Commit → MarkSucceeded → ACK

失败时：
HandleAsync 或 Commit 失败 → Rollback → MarkFailed → 重试或死信
```

### 7. 在 DI 中注册

RabbitMQ 事件总线：

```csharp
builder.Services.AddRabbitMqEventBus(options =>
{
    options.ConnectionString = "amqp://admin:admin123@localhost:5672";
}, typeof(Program).Assembly);
```

本地 MediatR 事件总线：

```csharp
builder.Services.AddMediatRLocalEventBus(typeof(Program).Assembly);
```

两者共存：

```csharp
builder.Services.AddMediatRLocalEventBus(typeof(Program).Assembly);
builder.Services.AddRabbitMqEventBus(options =>
{
    options.ConnectionString = "amqp://admin:admin123@localhost:5672";
}, typeof(Program).Assembly);
```

### 8. 发布事件

```csharp
public class OrderService
{
    private readonly IEventBus _eventBus;

    public async Task CompleteOrderAsync(Guid orderId)
    {
        // ...业务逻辑

        // 发布事件 — 框架自动路由到所有匹配的通道
        await _eventBus.PublishAsync(
            new OrderPaidEvent(orderId, amount),
            cancellationToken);
    }
}
```

### 9. 编写单元测试

```csharp
[Fact]
public async Task HandleAsync_ValidEvent_CreatesInvoice()
{
    var repo = new Mock<IRepository<Invoice, Guid>>();
    var logger = new Mock<ILogger<OrderPaidEventHandler>>();
    var handler = new OrderPaidEventHandler(logger.Object, repo.Object);

    await handler.HandleAsync(
        new OrderPaidEvent(Guid.NewGuid(), 100m),
        CancellationToken.None);

    repo.Verify(r => r.InsertAsync(It.IsAny<Invoice>()), Times.Once);
}
```

## 检查清单

- [ ] 事件实现了 `IEvent` 或 `IIntegrationEvent`
- [ ] 事件配置了路由特性（`[LocalEvent]` / `[RabbitMqEvent]`）
- [ ] Handler 实现了 `IEventHandler<TEvent>`
- [ ] 需要事务的 Handler 标记了 `[UnitOfWork]`
- [ ] 需要幂等的 Handler 标记了 `[IdempotentConsumer]`
- [ ] 需要重试的 RabbitMQ Handler 标记了 `[RabbitMqDeadLetterRetry]`
- [ ] **Handler 不手动控制消息确认**（ACK/Reject 由框架处理）
- [ ] **Handler 不手动提交事务**（由 `[UnitOfWork]` 控制）
- [ ] 文档注释使用中文
- [ ] 编写了单元测试

## 常见错误

- ❌ 忘记给事件标记路由特性 — 导致事件无法被路由到正确的通道
- ❌ Handler 中手动调用 `CommitAsync()` — 应由 `[UnitOfWork]` 统一管理
- ❌ 领域事件 Handler 中调用外部服务（如发邮件） — 应在集成事件 Handler 或 OnCompleted 回调中处理
- ❌ 在 `IEventHandler<T>` 实现中写消息确认逻辑 — 完全不需要，框架自动处理
