# 仓储与工作单元设计实现


## 概述

`Raindrop.Data` + `Raindrop.Data.EFCore` 提供基于 DDD 的仓储和工作单元实现，支持：

- **仓储（Repository）**：聚合根状态的持久化，不封装查询逻辑（查询用 `Expression` 谓词，批量更新/删除用原生 SQL 命令）
- **规约（Specification）**：可选的查询复用单元，用于需要 `Include` 导航属性的场景
- **工作单元（Unit of Work）**：事务边界，**递归领域事件处理**在 `SaveChanges` 之前完成，集成事件默认在事务提交之后尽力发布；启用 Outbox 时在提交前同事务登记
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
  ├─ DomainEventProcessor                  → 事件批次准备与迭代处理
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
    private readonly IUnitOfWorkFactory _unitOfWorkFactory;

    public OrderService(
        IRepository<Order, Guid> orderRepo,
        IRepository<Product, Guid> productRepo,
        IUnitOfWorkFactory unitOfWorkFactory)
    {
        _orderRepo = orderRepo;
        _productRepo = productRepo;
        _unitOfWorkFactory = unitOfWorkFactory;
    }

    public async Task CompleteOrderAsync(Guid orderId)
    {
        await using var uow = await _unitOfWorkFactory.BeginAsync();

        // 获取聚合根
        var order = await _orderRepo.GetAsync(orderId)
            ?? throw new InvalidOperationException("订单不存在。");

        // 领域方法 — 产生 OrderSubmitted 事件
        order.Complete();

        // 提交工作单元 → 递归处理领域事件 → SaveChanges → 集成事件 → OnCompleted
        await uow.CommitAsync();
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

`InsertAsync`、`UpdateAsync`、`DeleteAsync` 是普通仓储写操作，只把变更放入当前 `DbContext` 的 ChangeTracker，真正的保存、领域事件递归处理、集成事件登记或发布和 `OnCompleted` 回调都由 `UnitOfWork.CommitAsync()` 统一完成。

`InsertAndGetIdAsync` 是一个明确的设计特例，主要用于需要提前拿到数据库自增 ID 的场景。它会在实体主键仍是临时值时立即调用 `SaveChangesAsync()`，让数据库生成主键并回填实体。这个方法只表达“取号/拿 ID”，不代表完整业务提交，也不会触发 `CommitAsync()` 中的领域事件递归处理、集成事件分离发布和 `OnCompleted` 流程。调用方如果在实体上提前挂了领域事件，需要知道这些事件不会因为这次取 ID 自动处理。

`ExecuteCmd` 当前是**实验性过渡 API**。它直接执行原生 SQL 命令，不等待 `UnitOfWork.CommitAsync()` 才发送到数据库；调用发生在同一 `DbContext` 已开启的显式事务内时，命令参加当前事务，后续回滚该事务会撤销其变更；如果在事务外执行，则后来开启的 UoW 无法回滚此前已提交的命令。后续如果仓储 API 收敛，这个方法可能会被移除或改成更明确的批量命令抽象。

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

## 工作单元入口

`IUnitOfWorkFactory.BeginAsync()` 在返回前开启工作单元。关系型 provider 同时开启数据库事务；InMemory 只提供生命周期，不提供关系型原子性。不能省略 Begin 后直接 Commit。

```csharp
public interface IUnitOfWork : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken = default);
    Task RollbackAsync(CancellationToken cancellationToken = default);
    void OnCompleted(Func<Task> handler);
    void ClearCompletedHandler();
}

public interface IUnitOfWorkFactory
{
    ValueTask<IUnitOfWork> BeginAsync(CancellationToken cancellationToken = default);
}
```

同一个 DI scope 内，同一 DbContext 的仓储、工厂和 UoW 共享 ChangeTracker 与事务。重复 Begin 同一个活动 UoW 是加入原事务，不是独立的嵌套事务，也没有嵌套提交/回滚所有权。

```csharp
await using var uow = await unitOfWorkFactory.BeginAsync(cancellationToken);
try
{
    await orderRepository.InsertAsync(order);
    await inventoryRepository.UpdateAsync(inventory);
    await uow.CommitAsync(cancellationToken);
}
catch
{
    await uow.RollbackAsync(cancellationToken);
    throw;
}
```

`await using` 会兜底释放并尝试回滚未完成的事务。Commit 失败后不能修正内存状态并在原 UoW 上重试，必须丢弃当前 scope。

## 事件准备与提交流程

```text
Begin：开启事务
  → 收集并清空跟踪实体的事件
  → 分类领域事件与集成事件
  → 顺序执行领域事件 handler，继续收集新事件直到稳定
  → 集成事件：启用 Outbox 时登记到原事务；默认模式暂存到提交后
  → SaveChanges
  → 物理 Commit
  → 释放事务资源
  → 默认模式尽力发送集成事件
  → 按顺序执行 OnCompleted
```

| 事件/操作 | 默认模式 | 启用 Outbox |
|---|---|---|
| 普通领域事件 | 保存前在当前调用中处理 | 同样保存前处理，只允许本地路由 |
| 聚合集成事件 | 提交后尽力发送，失败只记日志 | 提交前冻结并同事务登记，不即时发送 |
| 直接 `IEventBus.PublishAsync` 集成事件 | 即时按路由发送 | 在活动 UoW 中使用同一个登记器 |
| `OnCompleted` | 确认提交后尽力执行 | 相同，不承担持久交付 |

框架最多处理 1024 轮非空事件批次，并检查取消。领域 handler 新增或修改的跟踪实体、再次产生的事件都纳入本次循环；只有集成事件的批次也会完整收集。超限或 handler 异常在提交前失败。没有事件时可以不注册 EventBus；存在事件但缺少总线时明确失败。

例如，订单聚合产生 `OrderSubmitted`，本地 handler 修改库存并产生 `StockReduced`，后者继续在当前 UoW 中处理。循环预算不分析业务因果，也不提供业务幂等；业务需要自己避免事件互相触发及重复执行。消费端的幂等特性不等于本地领域 handler 自动获得幂等保护。

### 内部组件

| 组件 | 职责 |
|---|---|
| `DomainEventBatch` | 纯分类，产生两类不可变事件批次，保持各自顺序 |
| `DomainEventProcessor` | 取出实体事件、迭代调用本地处理链、累积并准备集成事件 |
| `EventBusDispatch` | 缓存运行时类型到泛型 Publish 的委托，保留原始处理异常 |
| `PreparedEventDispatch` | 保存默认模式的提交后尽力发送；Outbox 模式不携带后置集成发送 |
| `EntityFrameworkCoreUnitOfWork<TDbContext>` | 管理生命周期、唯一物理事务、提交顺序和完成回调 |
| `UnitOfWorkSession<TDbContext>` | 共享阶段、提交尝试身份及消息身份 |
| `OutboxTransactionBinding` | 启用 Outbox 时约束 scope 内唯一活动 UoW，并校验实际 DbContext 归属 |

纯分类和不可变批次负责数据组织，取出/清空事件、调用 handler、保存和提交保留显式副作用顺序。UoW 不承担路由解析、消息序列化或后台重试。[本地消息表与登记](transactional-outbox.md)维护 Outbox 的完整实现契约。

## 提交结果与生命周期

正常流程为 `Ready → Active → Preparing → Saving → Committing → Completing → Ready`，开启和资源清理期间还有内部过渡状态。操作必须顺序执行，不提供共享 DbContext 的跨线程同步。

| 情况 | 处理 |
|---|---|
| 准备/保存失败或取消 | 保留原异常，尝试回滚、丢弃完成回调，实例进入 Failed |
| 关系型物理 Commit 调用抛异常 | 报告 `UnitOfWorkCommitUnknownException`，进入 Unknown |
| 已确认提交后的发送、回调或事务释放失败 | 只尝试记录诊断，不能改写已提交结果或重跑业务 |
| 日志提供者抛异常 | 隔离日志故障，不遮蔽原始异常，不中断剩余清理/回调 |
| 正常主动回滚 | 清空跟踪状态与回调，回到 Ready，可重新 Begin |
| Failed/Unknown 后回滚 | 只清理资源，保持终态，必须丢弃 scope |

Unknown 异常保留原 `CommitAttemptId`、已分配的 `MessageIds` 以及原异常。它们是核实线索，不是提交成功证明；回滚尝试或暂时查不到消息都不能证明此前没有提交，框架不自动重做业务。

准备/提交期间拒绝重入 Begin、Commit、Rollback 和 Dispose。完成回调可在 Ready、Active、Preparing 阶段登记或清理，保存及后置阶段拒绝修改。Outbox 模式最终保存开始后关闭登记窗口，保存拦截器/回调中的迟到发布使本次提交失败，即使调用方捕获该异常也不能继续提交。

启用 Outbox 时，同一 scope 只允许一个活动 Raindrop UoW，登记必须使用配置的消息 DbContext 的同一实例。其他 DbContext 的显式类型化 UoW 可顺序执行不含集成发布的事务；非泛型 UoW/Factory 别名不因发布事件自动切换上下文。Outbox 装配不一致或手工构造而缺少共享事务会话时拒绝 Begin。

### 资源所有权

UoW 只拥有自己开启的事务。DbContext 由创建它的 DI scope 或调用方释放，UoW 不提前释放借用的 DbContext。

Rollback 和 Dispose 会清空 ChangeTracker，丢弃未完成的新增、修改和删除，防止借用的 DbContext 后续保存时重放已放弃的变更；已提交的数据不受影响。清理使用独立于已取消业务 token 的资源释放路径；回滚、释放或诊断失败不能遮蔽原提交异常，也不能跳过跟踪清理。

## 关系型事务共享

```csharp
public interface IRelationalUnitOfWork : IUnitOfWork
{
    DbConnection Connection { get; }
    DbTransaction Transaction { get; }
}
```

EF Core UoW 实现该接口。需要与原生 SQL/Dapper 共用事务时，必须使用它暴露的同一连接和事务；只复制连接字符串再创建连接不能加入原事务。

```csharp
await using var uow = await unitOfWorkFactory.BeginAsync(cancellationToken);
var relational = (IRelationalUnitOfWork)uow;
await relational.Connection.ExecuteAsync(sql, parameters, relational.Transaction);
await uow.CommitAsync(cancellationToken);
```

上例的 `ExecuteAsync` 来自应用引用的 Dapper。非关系型 provider 不提供该关系型事务保证。

## OnCompleted 与自动入口

`OnCompleted` 是内存中的尽力后置钩子，按注册顺序执行，每个回调失败后继续处理其余回调，随后清空列表。

```csharp
await using var uow = await unitOfWorkFactory.BeginAsync(cancellationToken);
uow.OnCompleted(() => cacheService.InvalidateAsync("order-summary"));
await uow.CommitAsync(cancellationToken);
```

它适合不要求持久恢复的缓存失效等操作。需要持久保存的集成发布必须在活动 UoW 中登记，不能把 `OnCompleted` 当作可靠派发或结果通知存储。

RabbitMQ handler 上的 `[UnitOfWork]` 由消费执行器识别，在 handler 前 Begin，成功后 Commit；详见 [RabbitMQ 消费组件](rabbitmq-consumer-hostedservice.md)。HTTP Filter/EndpointFilter 自动 UoW 尚未实现，声明特性本身不会自动为 HTTP 开事务。

## DI 注册与测试

```csharp
services.AddRaindropData<MyDbContext>(options => options.UseNpgsql(connectionString));

// 聚合根不在 DbContext 所在程序集时，显式指定扫描程序集。
services.AddRaindropData<MyDbContext>(
    options => options.UseNpgsql(connectionString),
    typeof(DomainEntity).Assembly);
```

手动构造默认模式的 EF UoW 时，需要传入 `DomainEventProcessor`；Outbox 模式使用上述 DI 装配共享会话。关系型 provider 由 EF 能力判断，InMemory 不能验证物理事务保证。

`test/Raindrop.Data.Tests` 覆盖仓储写入、规约、事件迭代、取消、回调、资源所有权、提交 Unknown、故障诊断和 Outbox 事务归属。集成测试连接配置见[工程规范](engineering-guidelines.md#11-测试约定)。
