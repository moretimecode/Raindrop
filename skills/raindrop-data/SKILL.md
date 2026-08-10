---
name: raindrop-data
description: Raindrop DDD 领域建模基础。当用户需要 Entity、AggregateRoot、领域事件、业务规则、仓储、规约、分页等 DDD 构建块时使用。
---

# Raindrop.Data — DDD 领域建模基础

## NuGet 包

```
Raindrop.Data
Raindrop.Data.EFCore
```

## 实体与聚合根

```csharp
using Raindrop.Data.Entities;
using Raindrop.Data.Events;

// 聚合根 — 有 Id，承载领域事件
public sealed class Order : AggregateRoot<Guid>
{
    public Order(Guid id, Guid buyerId)
    {
        Id = id;
        BuyerId = buyerId;
        AddDomainEvent(new OrderCreatedEvent(id, buyerId));
    }

    public Guid BuyerId { get; private set; }
    public OrderStatus Status { get; private set; }

    public void Complete()
    {
        Status = OrderStatus.Completed;
        AddDomainEvent(new OrderCompletedEvent(Id));
    }
}

// 实体 — 非聚合根的领域对象
public sealed class OrderItem : Entity<Guid>
{
    public Guid ProductId { get; }
    public int Quantity { get; }
}
```

领域事件操作：
- `entity.AddDomainEvent(@event)` — 挂载事件
- `entity.GetDomainEvents()` — 获取待处理事件
- `entity.ClearDomainEvents()` — 清空（框架自动调用）

## 领域事件标记

```csharp
// 纯标记，不绑定任何消息队列
public sealed record OrderCreatedEvent(Guid OrderId) : IEvent;

// 跨进程语义标记（仍不绑定具体中间件）
public sealed record OrderPaidIntegrationEvent(Guid OrderId) : IIntegrationEvent;
```

`IEvent` 与消息队列无关。事件如何分发由事件总线层决定（见 raindrop-event-bus）。

## 业务规则

```csharp
public sealed class BuyerMustExistRule : IBusinessRule
{
    public string ErrorMessage => "买家不存在";
    public int ErrorCode => 10001;

    public bool IsBroken() => _buyerRepo.GetAsync(BuyerId) is null;
}

// 在实体中校验
public void ChangeBuyer(Guid buyerId)
{
    this.CheckRule(new BuyerMustExistRule(buyerId));
    BuyerId = buyerId;
}
// 规则破坏 → BusinessRuleValidationException
```

## 分页

```csharp
using Raindrop.Data.Paging;

var request = new Pageable(pageIndex: 1, pageSize: 20, orderBy: "CreatedTime", isDesc: true);
// 结果：PageableResult<T>(items, totalCount, request)
```

## 规约

只在需要 `Include` 导航属性或复用复杂查询条件时使用。简单条件直接用 `QueryAsync<T>` 一行解决。

```csharp
public sealed class OrderWithItemsSpec : BaseSpecification<Order>
{
    public OrderWithItemsSpec(Guid orderId)
        : base(o => o.Id == orderId)
    {
        AddInclude(o => o.Items);
        AddInclude("Items.Product");
    }
}
```

## 仓储接口

```csharp
IRepository<TEntity, TKey> where TEntity : AggregateRoot<TKey>
{
    // 写
    Task InsertAsync(TEntity entity);
    Task UpdateAsync(TEntity entity);
    Task DeleteAsync(TEntity entity);
    Task<TPrimaryKey> InsertAndGetIdAsync(TEntity entity); // 立即保存拿自增 ID
    Task ExecuteCmd(string sql, params object[] parameters); // 批量更新/删除

    // 读
    Task<TEntity?> GetAsync(TPrimaryKey id);
    Task<List<TEntity>> GetAllAsync();
    Task<List<TQuery>> QueryAsync<TQuery>(Expression<Func<TQuery, bool>> predicate) where TQuery : class;
    Task<TQuery?> FirstOrDefaultAsync<TQuery>(Expression<Func<TQuery, bool>> predicate) where TQuery : class;
    Task<List<TEntity>> GetListAsync(ISpecification<TEntity> specification);

    // 工作单元
    IUnitOfWork UnitOfWork { get; }
}
```

## 查询策略速查

| 场景 | 方式 |
|------|------|
| 按主键 | `GetAsync(id)` |
| 简单条件 | `QueryAsync<T>(o => o.Status == Paid)` |
| 需要 Include | `GetListAsync(new OrderWithItemsSpec(id))` |
| 批量更新/删除 | `ExecuteCmd("UPDATE orders SET status=1 WHERE expired=true")` |

## DI 注册

```csharp
// 一步完成：DbContext + 工作单元 + 所有聚合根仓储
services.AddRaindropData<MyDbContext>(options =>
    options.UseNpgsql(connectionString));

// 聚合根不在 DbContext 所在程序集时显式指定
services.AddRaindropData<MyDbContext>(
    options => options.UseNpgsql(connectionString),
    typeof(DomainEntity).Assembly);
```

## 使用示例

```csharp
public class OrderService
{
    private readonly IRepository<Order, Guid> _orderRepo;

    public async Task CompleteOrderAsync(Guid orderId)
    {
        var order = await _orderRepo.GetAsync(orderId);
        order.Complete();  // 产生领域事件
        await _orderRepo.UnitOfWork.CommitAsync();  // 递归处理事件 → SaveChanges
    }
}
```

## 原则

- 仓储只负责聚合根持久化，不封装查询逻辑
- Expression 谓词直接暴露 EF Core 查询能力
- 规约只在需要 Include 或复用复杂条件时用
- `IEvent` 不绑定任何消息队列
