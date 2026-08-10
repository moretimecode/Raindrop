---
name: add-domain-entity
description: 在 Raindrop 项目中添加领域实体（Entity）、聚合根（AggregateRoot）或业务规则（BusinessRule）。当用户需要定义新领域模型、添加聚合根、或编写业务规则时使用。触发词：添加实体、新增 Entity、创建聚合根、领域模型、业务规则、Add Entity、AggregateRoot。
---

# 添加领域实体与聚合根

在 Raindrop 项目中添加领域实体、聚合根或业务规则。

## 前置知识

参考文档：
- [docs/raindrop-data-usage.md](docs/raindrop-data-usage.md) — Raindrop.Data 使用说明
- [docs/repository-and-unitofwork.md](docs/repository-and-unitofwork.md) — 仓储与工作单元设计
- [project-overview](.claude/skills/raindrop-project-overview/SKILL.md) — 项目架构总览

## 步骤

### 1. 确定实体类型

| 类型 | 基类 | 适用场景 |
|------|------|----------|
| 简单实体 | `Entity<TKey>` | 有唯一标识但不作为聚合入口的实体 |
| 聚合根 | `AggregateRoot<TKey>` | 聚合的根实体，包含领域事件 |

### 2. 创建聚合根

```csharp
using Raindrop.Data.Entities;
using Raindrop.Data.Events;

public sealed class Order : AggregateRoot<Guid>
{
    // 使用 private set 保护属性
    public string CustomerName { get; private set; }
    public decimal Amount { get; private set; }
    public OrderStatus Status { get; private set; }

    // 私有构造器供 EF Core 使用
    private Order() { }

    // 公开构造器 — 创建新订单
    public Order(string customerName, decimal amount)
    {
        Id = Guid.NewGuid();
        CustomerName = customerName;
        Amount = amount;
        Status = OrderStatus.Pending;

        // 发布领域事件
        AddDomainEvent(new OrderCreatedEvent(Id, customerName));
    }

    // 领域行为 — 完成订单
    public void Complete()
    {
        Status = OrderStatus.Completed;
        AddDomainEvent(new OrderCompletedEvent(Id));
    }

    // 领域行为 — 带业务规则校验
    public void ChangeAmount(decimal newAmount)
    {
        this.CheckRule(new OrderMustBePendingRule(Status));
        Amount = newAmount;
    }
}
```

### 3. 定义领域事件

领域事件实现 `IEvent` 接口：

```csharp
using Raindrop.Data.Events;

// 普通领域事件 — 在同一事务内处理
public sealed record OrderCreatedEvent(
    Guid OrderId,
    string CustomerName) : IEvent;

// 集成事件 — 跨服务通信
public sealed record OrderCompletedIntegrationEvent(
    Guid OrderId) : IIntegrationEvent;
```

### 4. 定义业务规则

```csharp
using Raindrop.Data.Rules;

public sealed class OrderMustBePendingRule : IBusinessRule
{
    private readonly OrderStatus _status;

    public OrderMustBePendingRule(OrderStatus status)
    {
        _status = status;
    }

    public string ErrorMessage => "只能修改待处理状态的订单。";

    public int ErrorCode => 20001;

    public bool IsBroken() => _status != OrderStatus.Pending;
}
```

在实体中使用规则：

```csharp
public void Cancel()
{
    this.CheckRule(new OrderMustBePendingRule(Status));
    Status = OrderStatus.Cancelled;
    AddDomainEvent(new OrderCancelledEvent(Id));
}
```

规则被破坏时抛出 `BusinessRuleValidationException`。

### 5. 添加 EF Core 仓储（如果需要）

在 `Raindrop.Data.EFCore` 中不需要为每个聚合根写单独的仓储类。通过 DI 直接注入泛型仓储：

```csharp
public class OrderService
{
    private readonly IRepository<Order, Guid> _orderRepo;

    public OrderService(IRepository<Order, Guid> orderRepo)
    {
        _orderRepo = orderRepo;
    }
}
```

`AddRaindropData<TDbContext>()` 会自动为所有聚合根注册仓储。

### 6. 编写单元测试

```csharp
[Fact]
public void CreateOrder_ValidParams_CreatesOrderWithEvent()
{
    var order = new Order("Test Customer", 100m);

    Assert.Equal("Test Customer", order.CustomerName);
    Assert.Equal(100m, order.Amount);
    Assert.Equal(OrderStatus.Pending, order.Status);

    var events = order.GetDomainEvents();
    Assert.Single(events);
    Assert.IsType<OrderCreatedEvent>(events.First());
}

[Fact]
public void CompleteOrder_PendingOrder_StatusChanges()
{
    var order = new Order("Test", 100m);
    order.Complete();

    Assert.Equal(OrderStatus.Completed, order.Status);
    Assert.Equal(2, order.GetDomainEvents().Count());
}

[Fact]
public void CancelOrder_CompletedOrder_ThrowsBusinessRuleException()
{
    var order = new Order("Test", 100m);
    order.Complete();

    Assert.Throws<BusinessRuleValidationException>(() => order.Cancel());
}
```

## 检查清单

- [ ] 聚合根继承 `AggregateRoot<TKey>`
- [ ] 属性使用 `private set`，通过领域方法修改状态
- [ ] 有私有无参构造器供 EF Core 使用
- [ ] 领域事件通过 `AddDomainEvent()` 发布
- [ ] 业务规则实现 `IBusinessRule` 接口
- [ ] 领域方法中通过 `this.CheckRule()` 校验业务规则
- [ ] `IEvent` 用于领域事件，`IIntegrationEvent` 用于跨服务事件
- [ ] 文档注释使用中文
- [ ] 编写了单元测试

## 不该放在领域模型中的内容

- ❌ 数据库访问逻辑（放在 Repository 实现中）
- ❌ EF Core 配置（放在 `IEntityTypeConfiguration<T>` 或 `OnModelCreating` 中）
- ❌ RabbitMQ / Kafka 路由配置（放在事件特性上）
- ❌ HTTP 响应模型（放在 Application/DTO 层）
- ❌ MediatR 命令/查询接口（用 `ICommand<T>` / `IQuery<T>` 而非 `IRequest<T>`）
