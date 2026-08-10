# 仓储与工作单元设计说明

## 概述

`Raindrop.Data` + `Raindrop.Data.EFCore` 提供基于 DDD 的仓储和工作单元实现，支持：

- **仓储（Repository）**：聚合根状态的持久化，不封装查询逻辑（查询用 `Expression` 谓词，批量更新/删除用原生 SQL 命令）
- **规约（Specification）**：可选的查询复用单元，用于需要 `Include` 导航属性的场景
- **工作单元（Unit of Work）**：事务边界，**递归领域事件处理**在 `SaveChanges` 之前完成，集成事件在事务提交之后发布
- **完成回调（OnCompleted）**：注册提交成功后执行的委托

## 依赖关系

```text
Raindrop.Data
  ├─ Entities: IAggregateRoot, AggregateRoot<TKey>
  ├─ Repository: IRepository<TEntity, TKey>
  └─ Specifications: ISpecification<T>, BaseSpecification<T>

Raindrop.UnitOfWork.Abstractions
  └─ IUnitOfWork (+ OnCompleted / ClearCompletedHandler)

Raindrop.Data.EFCore
  ├─ EntityFrameworkCoreRepository         → 实现 IRepository
  ├─ EntityFrameworkCoreUnitOfWork          → 实现 IUnitOfWork（含递归领域事件）
  ├─ DomainEventProcessor                  → 递归事件处理核心
  └─ DataServiceCollectionExtensions       → DI 注册
```

## 仓储（Repository）

### 接口定义

```csharp
public interface IRepository<TEntity, in TPrimaryKey>
    where TEntity : AggregateRoot<TPrimaryKey>
{
    // ── 写操作 ──

    /// <summary>插入聚合根。</summary>
    Task InsertAsync(TEntity entity);

    /// <summary>更新聚合根。</summary>
    Task UpdateAsync(TEntity entity);

    /// <summary>删除聚合根。</summary>
    Task DeleteAsync(TEntity entity);

    /// <summary>插入并立即保存以返回自增 ID。设计特例：不代表完整工作单元提交。</summary>
    Task<TPrimaryKey> InsertAndGetIdAsync(TEntity entity);

    /// <summary>执行原生 SQL 命令，用于批量更新或删除。实验性过渡 API。</summary>
    Task ExecuteCmd(string sql, params object[] parameters);

    // ── 查操作 ──

    /// <summary>查询所有。</summary>
    Task<List<TEntity>> GetAllAsync();

    /// <summary>按主键查询。</summary>
    Task<TEntity?> GetAsync(TPrimaryKey id);

    /// <summary>按谓词查询列表。TQuery 必须是 DbContext 注册的实体类型。</summary>
    Task<List<TQuery>> QueryAsync<TQuery>(Expression<Func<TQuery, bool>> predicate)
        where TQuery : class;

    /// <summary>按谓词查单条。TQuery 必须是 DbContext 注册的实体类型。</summary>
    Task<TQuery?> FirstOrDefaultAsync<TQuery>(Expression<Func<TQuery, bool>> predicate)
        where TQuery : class;

    /// <summary>按规约查询（支持 Include 导航属性、复用复杂条件）。</summary>
    Task<List<TEntity>> GetListAsync(ISpecification<TEntity> specification);

    // ── 工作单元 ──

    /// <summary>当前仓储关联的工作单元。</summary>
    IUnitOfWork UnitOfWork { get; }
}
```

### 使用示例

```csharp
public class OrderService
{
    private readonly IRepository<Order, Guid> _orderRepo;
    private readonly IRepository<Product, Guid> _productRepo;

    public OrderService(
        IRepository<Order, Guid> orderRepo,
        IRepository<Product, Guid> productRepo)
    {
        _orderRepo = orderRepo;
        _productRepo = productRepo;
    }

    public async Task CompleteOrderAsync(Guid orderId)
    {
        // 获取聚合根
        var order = await _orderRepo.GetAsync(orderId);

        // 领域方法 — 产生 OrderSubmitted 事件
        order.Complete();

        // 提交工作单元 → 递归处理领域事件 → SaveChanges → 集成事件 → OnCompleted
        await _orderRepo.UnitOfWork.CommitAsync();
    }
}
```

### 查询策略

| 场景 | 方式 | 示例 |
|------|------|------|
| 按主键 | `GetAsync(id)` | `repo.GetAsync(orderId)` |
| 简单条件 | `QueryAsync<T>(predicate)` | `repo.QueryAsync<Order>(o => o.Status == Status.Paid)` |
| 需要 Include 导航属性 | `GetListAsync(specification)` | `repo.GetListAsync(new OrderWithItemsSpec(orderId))` |
| 批量更新/删除 | `ExecuteCmd(sql)` | `repo.ExecuteCmd("UPDATE orders SET status = 1 WHERE expired = true")` |

### 写操作语义边界

`InsertAsync`、`UpdateAsync`、`DeleteAsync` 是普通仓储写操作，只把变更放入当前 `DbContext` 的 ChangeTracker，真正的保存、领域事件递归处理、集成事件发布和 `OnCompleted` 回调都由 `UnitOfWork.CommitAsync()` 统一完成。

`InsertAndGetIdAsync` 是一个明确的设计特例，主要用于需要提前拿到数据库自增 ID 的场景。它会在实体主键仍是临时值时立即调用 `SaveChangesAsync()`，让数据库生成主键并回填实体。这个方法只表达“取号/拿 ID”，不代表完整业务提交，也不会触发 `CommitAsync()` 中的领域事件递归处理、集成事件分离发布和 `OnCompleted` 流程。调用方如果在实体上提前挂了领域事件，需要知道这些事件不会因为这次取 ID 自动处理。

`ExecuteCmd` 当前是**实验性过渡 API**。它直接执行原生 SQL 命令，不等待 `UnitOfWork.CommitAsync()` 才发送到数据库；因此在当前实现中，后续调用 `RollbackAsync()` 不会撤销已经执行成功的 SQL 命令。后续如果仓储 API 收敛，这个方法可能会被移除或改成更明确的批量命令抽象。

## 规约（Specification）

### 适用场景

规约只在以下场景有用：

1. **需要 `Include` / `ThenInclude` 导航属性**
2. **同一查询条件在多个地方复用**

简单条件直接用 `QueryAsync<T>` 一行解决，不需要规约。

### 定义规约

```csharp
public sealed class OrderWithItemsSpec : BaseSpecification<Order>
{
    public OrderWithItemsSpec(Guid orderId)
        : base(o => o.Id == orderId)           // ← Criteria（WHERE 条件）
    {
        AddInclude(o => o.Items);              // ← Include 导航属性
        AddInclude("Items.Product");           // ← 字符串路径 Include
    }
}

public sealed class PaidOrdersSpec : BaseSpecification<Order>
{
    public PaidOrdersSpec()
        : base(o => o.Status == OrderStatus.Paid)
    {
        ApplyOrderByDescending(o => o.CreatedAt);
    }
}
```

### 使用规约

```csharp
var spec = new OrderWithItemsSpec(orderId);
var order = (await repo.GetListAsync(spec)).FirstOrDefault();
// 等价于：
// _dbContext.Orders
//     .Include(o => o.Items)
//     .Include("Items.Product")
//     .Where(o => o.Id == orderId)
//     .ToListAsync()
```

### 什么时候不用规约

```csharp
// ❌ 不需要规约——一行就够了
var orders = await repo.QueryAsync<Order>(o => o.CreatedAt > DateTime.UtcNow.AddDays(-7));

// ✅ 批量更新/删除使用 ExecuteCmd
await repo.ExecuteCmd("UPDATE orders SET status = 1 WHERE expired = true");
```

## 工作单元与递归领域事件

### 工作单元流程

```text
BeginAsync()
  │
  └─ 开启显式数据库事务（BeginTransactionAsync）
       InMemory 等不支持事务的 provider 跳过事务管理

CommitAsync()
  │
  ├─ 1. 从 ChangeTracker 收集领域事件
  │       IIntegrationEvent 分离出来暂存
  │
  ├─ 2. 递归发布领域事件（通过 IEventBus）
  │       handler 修改的聚合根继续产生新事件 → 回到步骤 2
  │       领域事件 handler 异常直接向上抛出 → 事务回滚
  │
  ├─ 3. SaveChangesAsync() — 所有修改写入数据库
  │
  ├─ 4. Commit DB transaction — 提交 BeginAsync 已开启的事务
  │
  ├─ 5. 发布集成事件（通过 IEventBus）
  │       事务已提交，集成事件失败会记录错误日志，但不会回滚数据库
  │
  └─ 6. 执行 OnCompleted handlers
```

### 关键设计

> **CommitAsync 失败后，当前 UoW 和 DbContext 不可继续使用**
>
> 领域事件在发布前已从实体上清除。如果 CommitAsync 因领域事件 handler 异常而失败，
> 调用方必须调用 `RollbackAsync()` 或直接丢弃当前 scope。**不要修正问题后在同一 UoW 上重试 CommitAsync**，
> 因为事件列表已清空，再次 CommitAsync 不会重新触发领域事件处理。

| 特性 | 时机 | 说明 |
|------|------|------|
| 领域事件（`IEvent`）处理 | SaveChanges **之前** | 处理过程中实体修改在 ChangeTracker 累积，属于同一个事务 |
| 集成事件（`IIntegrationEvent`）发布 | SaveChanges **之后** | 事务已提交，防止发了消息但数据库回滚 |
| `OnCompleted` 回调 | 集成事件发布 **之后** | 数据库事务已提交后执行；回调失败只记录错误日志，不会回滚数据库，也不会让 `CommitAsync` 抛出异常 |
| 递归终止条件 | `CollectDomainEvents()` 返回空 | 不做循环检测，依赖开发者幂等保护 |

### 递归示例

```csharp
// Order 聚合根
public class Order : AggregateRoot<Guid>
{
    public void Complete()
    {
        Status = OrderStatus.Completed;
        AddDomainEvent(new OrderSubmittedEvent(Id));
    }
}

// Product 聚合根
public class Product : AggregateRoot<Guid>
{
    public void ReduceStock(int quantity)
    {
        Stock -= quantity;
        AddDomainEvent(new StockReducedEvent(Id, quantity));
    }
}

// 事件处理
public class OrderSubmittedHandler : IEventHandler<OrderSubmittedEvent>
{
    private readonly IRepository<Product, Guid> _productRepo;

    public async Task HandleAsync(OrderSubmittedEvent eventData)
    {
        var product = await _productRepo.GetAsync(eventData.ProductId);
        product.ReduceStock(1);            // ← 产生 StockReduced 事件
        // 不需要提交——当前 UoW 的 CommitAsync 会递归处理
    }
}

// 调用方
var order = await _orderRepo.GetAsync(orderId);
order.Complete();
await _orderRepo.UnitOfWork.CommitAsync();
// 递归: OrderSubmitted → StockReduced → 终止
```

### 循环风险与幂等

```
A 事件 → 处理 A → 产生 B 事件
  → 处理 B → 产生 A 事件
  → 处理 A → 产生 B 事件
  → ... 无限递归直到栈溢出
```

当前框架不做循环检测。防止循环的手段：

- **事件处理程序只改自己的聚合根**
- **在 `StockReduced` 处理程序中检查库存是否已扣减**
- **使用 `IdempotentConsumerAttribute`**（如果走事件总线）

## 工作单元实例一致性

同一个 DI scope 内的仓储和 `IUnitOfWorkFactory.BeginAsync()` 返回的工作单元共享同一个 EF Core `DbContext` 和 ChangeTracker。手动开启工作单元时，`BeginAsync()` 会在返回前初始化事务；仓储随后产生的变更都纳入这个工作单元。

### 手动工作单元入口

手动事务入口由 `IUnitOfWorkFactory` 表达：

```csharp
public interface IUnitOfWorkFactory
{
    ValueTask<IUnitOfWork> BeginAsync(CancellationToken cancellationToken = default);
}
```

`BeginAsync()` 必须在返回前完成工作单元初始化。对于 EF Core、ADO.NET、Dapper 等关系型实现来说，这意味着它要在内部打开连接并开启数据库事务；`CommitAsync()` 只负责提交已经开启的工作单元，不再承担“到提交时才开启事务”的职责。

即使底层 provider 不支持数据库事务，例如 EF Core InMemory，也必须先通过 `IUnitOfWorkFactory.BeginAsync()` 标记工作单元已经开启，再调用 `CommitAsync()`。这样开发、测试和关系型数据库环境保持同一套入口约束，避免 InMemory 测试通过但切换到 PostgreSQL 后才暴露漏掉 Begin 的问题。

调用方式：

```csharp
await using var uow = await unitOfWorkFactory.BeginAsync(cancellationToken);

await orderRepository.InsertAsync(order);
await inventoryRepository.UpdateAsync(inventory);

await uow.CommitAsync(cancellationToken);
```

如果中途失败，调用方可以显式回滚，也可以依赖 `DisposeAsync()` 做兜底回滚：

```csharp
await using var uow = await unitOfWorkFactory.BeginAsync(cancellationToken);

try
{
    await orderRepository.InsertAsync(order);
    await uow.CommitAsync(cancellationToken);
}
catch
{
    await uow.RollbackAsync(cancellationToken);
    throw;
}
```

### 自动工作单元入口

自动事务入口由特性和执行管道表达：

```csharp
[UnitOfWork]
public async Task CreateOrderAsync(...)
{
}
```

HTTP 场景可以由 Web 包提供 Filter 或 endpoint filter，在进入业务方法前开启工作单元，在业务方法成功后提交，异常时回滚。事件消费者场景应放在事件处理管道中，优先沉到 `Raindrop.EventBus.Core` 的通用 handler 执行逻辑里，让 RabbitMQ、MediatR、未来 Kafka 等适配器共享同一套工作单元规则。

自动工作单元的规则和手动入口一致：事务必须在 handler/action 执行前开启，成功后提交，失败后回滚。事件消费者必须保持 `Ack` 晚于 `CommitAsync()`。

### 关系型事务 seam

`IUnitOfWork` 保持通用，只表达提交、回滚、完成回调和释放，不直接暴露关系型数据库事务。关系型数据库需要额外 seam：

```csharp
public interface IRelationalUnitOfWork : IUnitOfWork
{
    DbConnection Connection { get; }

    DbTransaction Transaction { get; }
}
```

EF Core 的工作单元实现可以同时实现 `IUnitOfWork` 和 `IRelationalUnitOfWork`。这样普通业务只依赖 `IUnitOfWork`，而混用 EF 仓储、ADO.NET、Dapper 的场景可以从当前工作单元中取出同一个 `DbConnection` 和 `DbTransaction`：

```csharp
await using var uow = await unitOfWorkFactory.BeginAsync(cancellationToken);

var relational = (IRelationalUnitOfWork)uow;

await dapperConnection.ExecuteAsync(
    sql,
    param,
    transaction: relational.Transaction);

await uow.CommitAsync(cancellationToken);
```

这条 seam 不应该进入非关系型工作单元实现。非关系型适配器只需要实现 `IUnitOfWork`；关系型适配器在需要共享数据库事务时再额外实现 `IRelationalUnitOfWork`。

### 当前设计方向

- `IUnitOfWork` 保留，作为所有工作单元实现的核心接口。
- `IUnitOfWorkFactory` 作为手动开启入口。
- EF Core 工作单元的事务开启时机从 `CommitAsync()` 前移到 `BeginAsync()`。
- HTTP 和事件消费者通过 `[UnitOfWork]` 进入自动工作单元管道。

## OnCompleted 回调

```csharp
// 注册
var uow = repo.UnitOfWork;
uow.OnCompleted(async () =>
{
    await _cacheService.InvalidateAsync("order-summary");
    await _emailService.SendAsync(...);
});

await uow.CommitAsync();
// 提交成功后依次执行 OnCompleted 注册的委托
// 提交失败时不会执行
```

`OnCompleted` 在数据库事务提交后执行，属于尽力而为的后置钩子。如果某个回调抛异常，工作单元只记录错误日志并继续执行后续回调；数据库不会回滚，`CommitAsync` 也不会把该异常继续抛给调用方。对 RabbitMQ 等消息消费者来说，OnCompleted 失败不会导致已经提交的数据被重新消费或无限重试。

典型用途：
- 缓存失效
- 发送通知
- 日志记录
- 任何不要求与业务事务严格一致的后置操作

## DI 注册

```csharp
// 内置自动扫描：DbContext + 工作单元 + 所有聚合根仓储，一步完成
services.AddRaindropData<MyDbContext>(options =>
    options.UseNpgsql(connectionString));

// 如果聚合根不在 DbContext 所在程序集，显式指定额外程序集
services.AddRaindropData<MyDbContext>(
    options => options.UseNpgsql(connectionString),
    typeof(DomainEntity).Assembly);
```
