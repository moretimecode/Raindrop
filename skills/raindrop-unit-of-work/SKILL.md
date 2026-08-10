---
name: raindrop-unit-of-work
description: Raindrop 工作单元与事务管理。当用户需要管理数据库事务边界、递归领域事件处理、集成事件延迟发布、OnCompleted 回调时使用。
---

# Raindrop.UnitOfWork — 工作单元与事务

## NuGet 包

```
Raindrop.UnitOfWork.Abstractions
Raindrop.Data.EFCore              — EF Core 实现
```

## 核心接口

```csharp
public interface IUnitOfWork
{
    Task CommitAsync(CancellationToken ct = default);  // 递归事件 → SaveChanges → 集成事件 → OnCompleted
    void OnCompleted(Func<Task> handler);
    void ClearCompletedHandler();
}

public interface IUnitOfWorkFactory
{
    ValueTask<IUnitOfWork> BeginAsync(CancellationToken ct = default);
}

// 关系型数据库 seam（Dapper 需要共享事务时用）
public interface IRelationalUnitOfWork : IUnitOfWork
{
    DbConnection Connection { get; }
    DbTransaction Transaction { get; }
}
```

## CommitAsync 流程

```
1. 从 ChangeTracker 收集领域事件（IIntegrationEvent 分离暂存）
2. 递归发布领域事件 → handler 修改的聚合根继续产生新事件
3. SaveChangesAsync()
4. 提交数据库事务
5. 发布集成事件（事务已提交，失败只记日志不回滚）
6. 执行 OnCompleted 回调
```

## 使用方式

### 手动工作单元

```csharp
await using var uow = await unitOfWorkFactory.BeginAsync(ct);

await orderRepository.InsertAsync(order);
await inventoryRepository.UpdateAsync(inventory);

await uow.CommitAsync(ct);

// 异常处理
try { await uow.CommitAsync(ct); }
catch { await uow.RollbackAsync(ct); throw; }
```

### 自动工作单元 — [UnitOfWork]

```csharp
[UnitOfWork]
public async Task CreateOrderAsync(CreateOrderCommand cmd)
{
    // 管道自动开启事务，成功提交，异常回滚
}
```

HTTP 场景由 `Raindrop.AspNetCore` 提供 Filter；事件消费由 `Raindrop.EventBus.Core` 的 Executor 自动处理。

### OnCompleted 回调

```csharp
uow.OnCompleted(async () => await _cache.InvalidateAsync("orders"));
await uow.CommitAsync();
// 事务成功后执行 — 适合缓存失效、通知、日志
// 回调异常只记日志，不回滚事务，不抛异常
```

## 递归领域事件

```csharp
// Order 聚合根
order.Complete();  // → AddDomainEvent(OrderCompletedEvent)

// OrderCompletedHandler
product.ReduceStock(qty);  // → AddDomainEvent(StockReducedEvent)

// 单次 CommitAsync() 递归：
// OrderCompleted → StockReduced → (无新事件) → 终止
```

防循环：事件 handler 只改自己的聚合根 + 幂等检查。

## 关键规则

| 规则 | 说明 |
|------|------|
| CommitAsync 失败后不可重用 | 事件已清空，必须丢弃当前 scope |
| 领域事件在 SaveChanges 前处理 | handler 异常 → 事务回滚 |
| 集成事件在事务提交后 | 防止发了消息但 DB 回滚 |
| 同一 scope 内仓储共享 DbContext | 无论手动还是自动 |
| InMemory 也必须 BeginAsync | 保证开发/测试/生产一致 |
