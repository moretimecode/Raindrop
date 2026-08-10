# Raindrop.EventBus.Abstractions 设计说明

`Raindrop.EventBus.Abstractions` 定义事件发布、路由、处理器、消费幂等等基础契约，不绑定 MediatR、RabbitMQ、Kafka、BufferQueue 等具体实现。

基础事件标记 `IEvent` 位于 `Raindrop.Data.Events`。事件总线抽象依赖 `Raindrop.Data`，但 `Raindrop.Data` 不依赖事件总线。

## 核心规则

事件发布入口只有一个：

```csharp
public interface IEventBus
{
    Task PublishAsync<TEvent>(TEvent eventData, CancellationToken cancellationToken = default)
        where TEvent : IEvent;
}
```

路由规则由多个 `IEventRouteResolver` 增量叠加决定。不同实现包提供自己的 Resolver：

- `MediatREventRouteResolver`：只贡献本地事件路由。无任何路由特性的事件默认补成本地 `LocalEventAttribute` 路由；显式 `[LocalEvent]` 也会返回本地路由。
- `RabbitMqEventRouteResolver`：只贡献 RabbitMQ 事件路由。只有 `[RabbitMqEvent]` 才会返回 RabbitMQ 路由。

`RoutingEventBus` 会把所有 Resolver 返回的路由集合合并，再交给可以处理对应路由的 `IEventTransport`。
因此多个事件总线可以共存，不存在“只能选择一个 Resolver”的限制。

如果一个事件既要进入异步消息中间件，又要进入本地同步流程，就同时标记中间件特性和 `[LocalEvent]`。

## 事件类型

普通事件：

```csharp
using Raindrop.Data.Events;

public sealed record OrderPaidEvent(Guid OrderId) : IEvent;
```

跨进程或跨服务事件可以标记为 `IIntegrationEvent`：

```csharp
public sealed record OrderPaidIntegrationEvent(Guid OrderId) : IIntegrationEvent;
```

`IIntegrationEvent` 只是语义标记，不决定事件一定走哪个中间件。真正的投递目标仍然由路由特性和 Resolver 决定。

## 路由特性

所有事件路由特性都继承 `EventRouteAttribute`：

```csharp
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true, Inherited = false)]
public abstract class EventRouteAttribute : Attribute
{
}
```

本地事件特性：

```csharp
public sealed class LocalEventAttribute : EventRouteAttribute
{
}
```

示例：

```csharp
[LocalEvent]
[RabbitMqEvent("order", "order.created")]
public sealed record OrderCreatedEvent(Guid OrderId) : IIntegrationEvent;
```

`RabbitMqEventAttribute` 不放在抽象包里，由 `Raindrop.EventBus.RabbitMQ` 自己定义。

## 路由事件总线

`RoutingEventBus` 是通用实现，位于 `Raindrop.EventBus.Core`。

它只依赖：

```text
IEventBus
IEventRouteResolver
IEventTransport
```

因此它不属于 MediatR 包。MediatR、RabbitMQ、Kafka、BufferQueue 都可以通过 `IEventTransport` 接入这条通用路由管道。

核心注册方法支持增量注册路由解析器：

```csharp
services.AddEventBusCore<MediatREventRouteResolver>();
services.AddEventBusCore<RabbitMqEventRouteResolver>();
```

对应用者来说，通常不需要手动调用 `AddEventBusCore()`，而是使用具体实现包提供的注册方法：

```csharp
services.AddMediatRLocalEventBus(typeof(Program).Assembly);
services.AddRabbitMqEventBus(typeof(Program).Assembly);
```

## 程序集边界

当前建议结构：

```text
Raindrop.Data
  IEvent
  Entity
  AggregateRoot

Raindrop.EventBus.Abstractions
  IIntegrationEvent
  IEventBus
  IEventHandler<TEvent>
  EventRouteAttribute
  LocalEventAttribute
  EventRoute
  IEventRouteResolver
  IEventTransport
  IdempotentConsumerAttribute
  IConsumerIdempotencyStore

Raindrop.EventBus.Core
  RoutingEventBus
  EventHandlerRegistrationScanner
  PropertyPathConsumerIdempotencyKeyResolver
  InMemoryConsumerIdempotencyStore

Raindrop.EventBus.MediatR
  MediatREventRouteResolver
  MediatRLocalEventTransport

Raindrop.EventBus.RabbitMQ
  RabbitMqEventRouteResolver
  RabbitMqEventAttribute
  RabbitMqReceivedMessage
  RabbitMqConsumerExecutor
```
