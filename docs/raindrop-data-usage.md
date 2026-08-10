# Raindrop.Data 使用说明

`Raindrop.Data` 用来放领域模型和数据表达中最稳定、最底层的基础类型。它不负责数据库访问，也不绑定 Web、MediatR、RabbitMQ、Kafka 等具体实现。

当前主要包含：

- 实体与聚合根基础类型
- 领域事件基础标记
- 业务规则校验
- 分页与排序模型

## 依赖关系

`Raindrop.Data` 是底层包，不依赖事件总线。

```text
Raindrop.Data
  IEvent
  IEntity
  Entity<TKey>
  AggregateRoot<TKey>
  IBusinessRule
```

`IEvent` 位于 `Raindrop.Data.Events`，它只是领域事件和集成事件共享的基础标记，不表达事件总线、消息队列或发布机制。

```csharp
using Raindrop.Data.Events;

public sealed record OrderCreatedEvent(Guid OrderId) : IEvent;
```

## 实体与聚合根

实体可以保存领域过程中产生的事件，但不关心事件后续如何发布。

```csharp
using Raindrop.Data.Entities;
using Raindrop.Data.Events;

public sealed class Order : AggregateRoot<Guid>
{
    public Order(Guid id, Guid buyerId)
    {
        Id = id;
        BuyerId = buyerId;

        AddDomainEvent(new OrderCreatedEvent(id, buyerId));
    }

    public Guid BuyerId { get; private set; }
}

public sealed record OrderCreatedEvent(
    Guid OrderId,
    Guid BuyerId) : IEvent;
```

实体事件相关方法：

```csharp
order.AddDomainEvent(@event);
var events = order.GetDomainEvents();
order.ClearDomainEvents();
```

## 业务规则

业务规则通过 `IBusinessRule` 表达：

```csharp
using Raindrop.Data.Rules;

public sealed class BuyerMustExistRule : IBusinessRule
{
    public string ErrorMessage => "买家不能为空。";

    public int ErrorCode => 10001;

    public bool IsBroken()
    {
        return false;
    }
}
```

在实体中校验规则：

```csharp
using Raindrop.Data.Entities;

public sealed class Order : AggregateRoot<Guid>
{
    public void ChangeBuyer(Guid buyerId)
    {
        this.CheckRule(new BuyerMustExistRule());
    }
}
```

规则被破坏时会抛出 `BusinessRuleValidationException`。

## 分页与排序

分页请求使用 `Pageable`：

```csharp
using Raindrop.Data.Paging;

var request = new Pageable(
    pageIndex: 1,
    pageSize: 20,
    orderBy: "CreatedTime",
    isDesc: true);
```

分页结果使用 `PageableResult<T>`。

## 建议边界

适合放在 `Raindrop.Data`：

- `IEvent`
- `IEntity`
- `Entity<TKey>`
- `AggregateRoot<TKey>`
- `IBusinessRule`
- `BusinessRuleValidationException`
- `Pageable`
- `PageableResult<T>`

不建议放在 `Raindrop.Data`：

- 数据库仓储实现
- EF Core 配置
- HTTP 响应模型
- MediatR 命令和查询接口
- RabbitMQ、Kafka 等具体事件总线实现
