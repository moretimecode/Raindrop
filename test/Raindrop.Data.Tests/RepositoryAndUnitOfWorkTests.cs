using System.Linq.Expressions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Raindrop.Data.EFCore;
using Raindrop.Data.Entities;
using Raindrop.Data.Events;
using Raindrop.Data.Repository;
using Raindrop.Data.Specifications;
using Raindrop.EventBus.Abstractions;
using Raindrop.UnitOfWork.Abstractions;

namespace Raindrop.Data.Tests;

public class RepositoryAndUnitOfWorkTests
{
    // ===================================================================
    //  测试用的领域模型
    // ===================================================================

    public sealed class Order : AggregateRoot<Guid>
    {
        public Order(Guid id, string orderNumber, Guid? productId = null)
        {
            Id = id;
            OrderNumber = orderNumber;
            ProductId = productId;
        }

        // EF Core constructor
        private Order() { OrderNumber = string.Empty; }

        public string OrderNumber { get; private set; }
        public OrderStatus Status { get; private set; } = OrderStatus.Pending;
        public Guid? ProductId { get; private set; }

        public void Complete()
        {
            Status = OrderStatus.Completed;
            AddDomainEvent(new OrderSubmittedEvent(Id, ProductId));
        }

        public void Cancel()
        {
            Status = OrderStatus.Cancelled;
            AddDomainEvent(new OrderCancelledEvent(Id));
        }
    }

    public enum OrderStatus { Pending, Completed, Cancelled }

    public sealed record OrderSubmittedEvent(Guid OrderId, Guid? ProductId) : IEvent;
    public sealed record OrderCancelledEvent(Guid OrderId) : IEvent;
    public sealed record StockReducedEvent(Guid ProductId, int Quantity) : IEvent;
    public sealed record StockReducedIntegrationEvent(Guid ProductId, int Quantity) : IIntegrationEvent;

    public sealed class Product : AggregateRoot<Guid>
    {
        public Product(Guid id, string name, int stock)
        {
            Id = id;
            Name = name;
            Stock = stock;
        }

        private Product() { Name = string.Empty; }

        public string Name { get; private set; }
        public int Stock { get; private set; }

        public void ReduceStock(int quantity)
        {
            Stock -= quantity;
            AddDomainEvent(new StockReducedEvent(Id, quantity));
            AddDomainEvent(new StockReducedIntegrationEvent(Id, quantity));
        }
    }

    public sealed class AuditLog : AggregateRoot<Guid>
    {
        public AuditLog(Guid id, string message)
        {
            Id = id;
            Message = message;
        }

        private AuditLog() { Message = string.Empty; }

        public string Message { get; private set; }
    }

    // ===================================================================
    //  测试用的事件处理器
    // ===================================================================

    public sealed class OrderSubmittedHandler : IEventHandler<OrderSubmittedEvent>
    {
        private readonly IRepository<Product, Guid> _productRepo;

        public OrderSubmittedHandler(IRepository<Product, Guid> productRepo)
        {
            _productRepo = productRepo;
        }

        public async Task HandleAsync(OrderSubmittedEvent eventData, CancellationToken cancellationToken = default)
        {
            if (eventData.ProductId.HasValue)
            {
                var product = await _productRepo.GetAsync(eventData.ProductId.Value);
                product.ReduceStock(1);
            }
        }
    }

    public sealed class StockReducedHandler : IEventHandler<StockReducedEvent>
    {
        private readonly IRepository<AuditLog, Guid> _auditLogRepo;

        public StockReducedHandler(IRepository<AuditLog, Guid> auditLogRepo)
        {
            _auditLogRepo = auditLogRepo;
        }

        public async Task HandleAsync(StockReducedEvent eventData, CancellationToken cancellationToken = default)
        {
            await _auditLogRepo.InsertAsync(new AuditLog(Guid.NewGuid(),
                $"Stock reduced for product {eventData.ProductId} by {eventData.Quantity}"));
        }
    }

    public sealed class OrderCancelledHandler : IEventHandler<OrderCancelledEvent>
    {
        public Task HandleAsync(OrderCancelledEvent eventData, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    // ===================================================================
    //  DbContext
    // ===================================================================

    public sealed class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }

        public DbSet<Order> Orders => Set<Order>();
        public DbSet<Product> Products => Set<Product>();
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Order>(b =>
            {
                b.HasKey(e => e.Id);
                b.Property(e => e.OrderNumber).IsRequired().HasMaxLength(200);
                b.Property(e => e.Status).HasConversion<int>();
            });

            modelBuilder.Entity<Product>(b =>
            {
                b.HasKey(e => e.Id);
                b.Property(e => e.Name).IsRequired().HasMaxLength(200);
            });

            modelBuilder.Entity<AuditLog>(b =>
            {
                b.HasKey(e => e.Id);
                b.Property(e => e.Message).IsRequired();
            });
        }
    }

    // ===================================================================
    //  规约示例
    // ===================================================================

    private sealed class CompletedOrdersSpec : BaseSpecification<Order>
    {
        public CompletedOrdersSpec()
            : base(o => o.Status == OrderStatus.Completed)
        {
            ApplyOrderByDescending(o => o.OrderNumber);
        }
    }

    // ===================================================================
    //  Helpers
    // ===================================================================

    /// <summary>
    /// 创建测试服务提供者。事件总线使用无操作的内部转发器。
    /// 适用于不需要递归领域事件处理的简单测试。
    /// </summary>
    private static ServiceProvider CreateServiceProvider(string dbName, out CapturingEventBus eventBus)
    {
        var noopBus = new NoopEventBus();
        eventBus = new CapturingEventBus(noopBus);

        var services = new ServiceCollection();
        services.AddRaindropData<TestDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddSingleton<IEventBus>(eventBus);

        services.AddScoped<IEventHandler<OrderSubmittedEvent>, OrderSubmittedHandler>();
        services.AddScoped<IEventHandler<StockReducedEvent>, StockReducedHandler>();
        services.AddScoped<IEventHandler<OrderCancelledEvent>, OrderCancelledHandler>();

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// 创建使用 MediatR 作为事件总线的测试提供者。
    /// IEventBus 由 AddMediatRLocalEventBus 提供的 RoutingEventBus 实现。
    /// CapturingEventBus 作为附加的集成事件收集器（不作为主 IEventBus 注册）。
    /// </summary>
    private static ServiceProvider CreateMediatRServiceProvider(string dbName, out CapturingEventBus capturingBus)
    {
        capturingBus = new CapturingEventBus(new NoopEventBus());

        var services = new ServiceCollection();
        services.AddRaindropData<TestDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddScoped<IEventHandler<OrderSubmittedEvent>, OrderSubmittedHandler>();
        services.AddScoped<IEventHandler<StockReducedEvent>, StockReducedHandler>();
        services.AddScoped<IEventHandler<OrderCancelledEvent>, OrderCancelledHandler>();
        services.AddMediatRLocalEventBus(typeof(RepositoryAndUnitOfWorkTests).Assembly);
        // CapturingEventBus 只用于捕获集成事件，不作为 IEventBus 注册
        services.AddSingleton(capturingBus);

        return services.BuildServiceProvider();
    }

    private static async Task CommitAsync(IServiceProvider provider)
    {
        var uow = await provider.GetRequiredService<IUnitOfWorkFactory>().BeginAsync();
        await uow.CommitAsync();
    }

    /// <summary>
    /// 装饰器模式的事件总线，同时捕获和转发事件。
    /// 捕获的事件可供断言验证，转发到内部事件总线供 handler 处理。
    /// </summary>
    public sealed class CapturingEventBus : IEventBus
    {
        private readonly IEventBus _inner;

        public CapturingEventBus(IEventBus inner)
        {
            _inner = inner;
        }

        public List<object> PublishedEvents { get; } = [];

        public async Task PublishAsync<TEvent>(TEvent eventData, CancellationToken cancellationToken = default)
            where TEvent : IEvent
        {
            PublishedEvents.Add(eventData!);
            await _inner.PublishAsync(eventData, cancellationToken);
        }
    }

    /// <summary>
    /// 无操作的事件总线，用于简单测试的转发目标。
    /// </summary>
    public sealed class NoopEventBus : IEventBus
    {
        public Task PublishAsync<TEvent>(TEvent eventData, CancellationToken cancellationToken = default)
            where TEvent : IEvent
        {
            return Task.CompletedTask;
        }
    }

    // ===================================================================
    //  Repository 写操作测试
    // ===================================================================

    [Fact]
    public async Task InsertAsync_Then_GetAsync_ReturnsEntity()
    {
        var provider = CreateServiceProvider(nameof(InsertAsync_Then_GetAsync_ReturnsEntity), out _);
        var repo = provider.GetRequiredService<IRepository<Order, Guid>>();

        var id = Guid.NewGuid();
        await repo.InsertAsync(new Order(id, "ORD-001"));
        await CommitAsync(provider);

        var saved = await repo.GetAsync(id);
        Assert.NotNull(saved);
        Assert.Equal("ORD-001", saved.OrderNumber);
    }

    [Fact]
    public async Task UpdateAsync_ModifiesEntity()
    {
        var dbName = nameof(UpdateAsync_ModifiesEntity);
        var id = Guid.NewGuid();

        var provider1 = CreateServiceProvider(dbName, out _);
        var repo1 = provider1.GetRequiredService<IRepository<Order, Guid>>();
        await repo1.InsertAsync(new Order(id, "ORD-001"));
        await CommitAsync(provider1);
        await provider1.DisposeAsync();

        var provider2 = CreateServiceProvider(dbName, out _);
        var repo2 = provider2.GetRequiredService<IRepository<Order, Guid>>();
        var order = await repo2.GetAsync(id);
        order.Complete();
        await repo2.UpdateAsync(order);
        await CommitAsync(provider2);

        var provider3 = CreateServiceProvider(dbName, out _);
        var repo3 = provider3.GetRequiredService<IRepository<Order, Guid>>();
        var saved = await repo3.GetAsync(id);
        Assert.Equal(OrderStatus.Completed, saved.Status);
    }

    [Fact]
    public async Task DeleteAsync_RemovesEntity()
    {
        var dbName = nameof(DeleteAsync_RemovesEntity);
        var id = Guid.NewGuid();

        var provider1 = CreateServiceProvider(dbName, out _);
        var repo1 = provider1.GetRequiredService<IRepository<Order, Guid>>();
        await repo1.InsertAsync(new Order(id, "ORD-001"));
        await CommitAsync(provider1);

        var order = await repo1.GetAsync(id);
        await repo1.DeleteAsync(order);
        await CommitAsync(provider1);

        var saved = await repo1.GetAsync(id);
        Assert.Null(saved);
    }

    [Fact]
    public async Task InsertAndGetIdAsync_ReturnsGeneratedId()
    {
        var dbName = nameof(InsertAndGetIdAsync_ReturnsGeneratedId);
        var provider = CreateServiceProvider(dbName, out _);
        var repo = provider.GetRequiredService<IRepository<Order, Guid>>();

        var id = Guid.NewGuid();
        var returnedId = await repo.InsertAndGetIdAsync(new Order(id, "ORD-001"));

        Assert.Equal(id, returnedId);
    }

    // ===================================================================
    //  Repository 查询测试
    // ===================================================================

    [Fact]
    public async Task QueryAsync_FiltersByPredicate()
    {
        var dbName = nameof(QueryAsync_FiltersByPredicate);
        var provider = CreateServiceProvider(dbName, out _);
        var repo = provider.GetRequiredService<IRepository<Order, Guid>>();

        await repo.InsertAsync(new Order(Guid.NewGuid(), "ORD-A"));
        await repo.InsertAsync(new Order(Guid.NewGuid(), "ORD-B"));
        await CommitAsync(provider);

        var results = await repo.QueryAsync<Order>(o => o.OrderNumber == "ORD-A");
        var result = Assert.Single(results);
        Assert.Equal("ORD-A", result.OrderNumber);
    }

    [Fact]
    public async Task FirstOrDefaultAsync_WithPredicate_ReturnsMatching()
    {
        var dbName = nameof(FirstOrDefaultAsync_WithPredicate_ReturnsMatching);
        var provider = CreateServiceProvider(dbName, out _);
        var repo = provider.GetRequiredService<IRepository<Order, Guid>>();

        await repo.InsertAsync(new Order(Guid.NewGuid(), "ORD-A"));
        await CommitAsync(provider);

        var result = await repo.FirstOrDefaultAsync<Order>(o => o.OrderNumber == "ORD-A");
        Assert.NotNull(result);
        Assert.Equal("ORD-A", result.OrderNumber);

        var notFound = await repo.FirstOrDefaultAsync<Order>(o => o.OrderNumber == "NONEXISTENT");
        Assert.Null(notFound);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsAll()
    {
        var dbName = nameof(GetAllAsync_ReturnsAll);
        var provider = CreateServiceProvider(dbName, out _);
        var repo = provider.GetRequiredService<IRepository<Order, Guid>>();

        await repo.InsertAsync(new Order(Guid.NewGuid(), "ORD-A"));
        await repo.InsertAsync(new Order(Guid.NewGuid(), "ORD-B"));
        await repo.InsertAsync(new Order(Guid.NewGuid(), "ORD-C"));
        await CommitAsync(provider);

        var all = await repo.GetAllAsync();
        Assert.Equal(3, all.Count);
    }

    // ===================================================================
    //  Specification 测试
    // ===================================================================

    [Fact]
    public async Task GetListAsync_WithSpecification_FiltersAndOrders()
    {
        var dbName = nameof(GetListAsync_WithSpecification_FiltersAndOrders);
        var provider = CreateServiceProvider(dbName, out _);
        var repo = provider.GetRequiredService<IRepository<Order, Guid>>();

        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var id3 = Guid.NewGuid();

        var o1 = new Order(id1, "ORD-C"); o1.Complete();
        var o2 = new Order(id2, "ORD-A"); o2.Complete();
        var o3 = new Order(id3, "ORD-B"); // Pending

        await repo.InsertAsync(o1);
        await repo.InsertAsync(o2);
        await repo.InsertAsync(o3);
        await CommitAsync(provider);

        var spec = new CompletedOrdersSpec();
        var results = await repo.GetListAsync(spec);

        Assert.Equal(2, results.Count);
        Assert.Equal("ORD-C", results[0].OrderNumber);
        Assert.Equal("ORD-A", results[1].OrderNumber);
    }

    // ===================================================================
    //  UnitOfWork 属性
    // ===================================================================

    [Fact]
    public void Repository_Exposes_UnitOfWork()
    {
        var provider = CreateServiceProvider(nameof(Repository_Exposes_UnitOfWork), out _);
        var repo = provider.GetRequiredService<IRepository<Order, Guid>>();

        Assert.NotNull(repo.UnitOfWork);
    }

    [Fact]
    public async Task CommitAsync_WithoutBegin_ThrowsForInMemoryProvider()
    {
        var provider = CreateServiceProvider(nameof(CommitAsync_WithoutBegin_ThrowsForInMemoryProvider), out _);
        var repo = provider.GetRequiredService<IRepository<Order, Guid>>();

        await repo.InsertAsync(new Order(Guid.NewGuid(), "ORD-NO-BEGIN"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.UnitOfWork.CommitAsync());
    }

    [Fact]
    public async Task SameScope_UsesSameUnitOfWork()
    {
        var provider = CreateServiceProvider(nameof(SameScope_UsesSameUnitOfWork), out _);

        var orderRepo = provider.GetRequiredService<IRepository<Order, Guid>>();
        var productRepo = provider.GetRequiredService<IRepository<Product, Guid>>();

        Assert.Same(orderRepo.UnitOfWork, productRepo.UnitOfWork);
    }

    // ===================================================================
    //  递归领域事件处理
    // ===================================================================

    [Fact]
    public async Task CommitAsync_WithRecursiveDomainEvents_ProcessesAllEvents()
    {
        var dbName = nameof(CommitAsync_WithRecursiveDomainEvents_ProcessesAllEvents);
        var provider = CreateMediatRServiceProvider(dbName, out var capturingBus);
        var orderRepo = provider.GetRequiredService<IRepository<Order, Guid>>();

        var productId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        // 准备数据
        await provider.GetRequiredService<IRepository<Product, Guid>>().InsertAsync(
            new Product(productId, "Widget", 10));
        await orderRepo.InsertAsync(new Order(orderId, "ORD-001", productId));

        // 提交种子数据
        await CommitAsync(provider);

        // 用同一个 provider 继续（InMemory 数据还在，ChangeTracker 已清空）
        // 执行 Complete → 触发递归事件处理
        var order = await orderRepo.GetAsync(orderId);
        order.Complete();
        await CommitAsync(provider);

        // 验证递归结果
        Assert.Equal(OrderStatus.Completed, order.Status);

        // Product 库存已扣减（递归：OrderSubmitted → StockReduced）
        var savedProduct = await provider.GetRequiredService<IRepository<Product, Guid>>().GetAsync(productId);
        Assert.NotNull(savedProduct);
        Assert.Equal(9, savedProduct.Stock);

        // AuditLog 已创建（递归：StockReduced → AuditLog insert，不产生新事件 → 终止）
        var logs = await provider.GetRequiredService<IRepository<AuditLog, Guid>>().GetAllAsync();
        Assert.NotEmpty(logs);
    }

    [Fact]
    public async Task CommitAsync_WithoutDomainEvents_SavesDirectly()
    {
        var dbName = nameof(CommitAsync_WithoutDomainEvents_SavesDirectly);
        var provider = CreateServiceProvider(dbName, out var eventBus);
        var repo = provider.GetRequiredService<IRepository<Order, Guid>>();

        await repo.InsertAsync(new Order(Guid.NewGuid(), "ORD-NO-EVENT"));
        await CommitAsync(provider);

        Assert.Empty(eventBus.PublishedEvents);
    }

    // ===================================================================
    //  OnCompleted 回调
    // ===================================================================

    [Fact]
    public async Task OnCompleted_RunsAfterSuccessfulCommit()
    {
        var dbName = nameof(OnCompleted_RunsAfterSuccessfulCommit);
        var provider = CreateServiceProvider(dbName, out _);
        var repo = provider.GetRequiredService<IRepository<Order, Guid>>();

        var completed = false;
        repo.UnitOfWork.OnCompleted(async () =>
        {
            completed = true;
            await Task.CompletedTask;
        });

        await repo.InsertAsync(new Order(Guid.NewGuid(), "ORD-ONCOMPLETED"));
        await CommitAsync(provider);

        Assert.True(completed);
    }

    [Fact]
    public async Task OnCompleted_DoesNotRunOnRollback()
    {
        var dbName = nameof(OnCompleted_DoesNotRunOnRollback);
        var provider = CreateServiceProvider(dbName, out _);
        var repo = provider.GetRequiredService<IRepository<Order, Guid>>();

        var completed = false;
        repo.UnitOfWork.OnCompleted(async () =>
        {
            completed = true;
            await Task.CompletedTask;
        });

        await repo.InsertAsync(new Order(Guid.NewGuid(), "ORD-ROLLBACK"));
        await repo.UnitOfWork.RollbackAsync();

        Assert.False(completed);

        var all = await repo.GetAllAsync();
        Assert.Empty(all);
    }

    [Fact]
    public async Task MultipleOnCompleted_ExecuteInOrder()
    {
        var dbName = nameof(MultipleOnCompleted_ExecuteInOrder);
        var provider = CreateServiceProvider(dbName, out _);
        var repo = provider.GetRequiredService<IRepository<Order, Guid>>();

        var executionOrder = new List<int>();

        repo.UnitOfWork.OnCompleted(async () => { executionOrder.Add(1); await Task.CompletedTask; });
        repo.UnitOfWork.OnCompleted(async () => { executionOrder.Add(2); await Task.CompletedTask; });
        repo.UnitOfWork.OnCompleted(async () => { executionOrder.Add(3); await Task.CompletedTask; });

        await repo.InsertAsync(new Order(Guid.NewGuid(), "ORD-MULTI"));
        await CommitAsync(provider);

        Assert.Equal([1, 2, 3], executionOrder);
    }

    [Fact]
    public async Task ClearCompletedHandler_RemovesAllHandlers()
    {
        var dbName = nameof(ClearCompletedHandler_RemovesAllHandlers);
        var provider = CreateServiceProvider(dbName, out _);
        var repo = provider.GetRequiredService<IRepository<Order, Guid>>();

        var completed = false;
        repo.UnitOfWork.OnCompleted(async () => { completed = true; await Task.CompletedTask; });
        repo.UnitOfWork.ClearCompletedHandler();

        await repo.InsertAsync(new Order(Guid.NewGuid(), "ORD-CLEAR"));
        await CommitAsync(provider);

        Assert.False(completed);
    }

    //  MultipleRepositories share the same UoW transaction
    // ===================================================================

    [Fact]
    public async Task MultipleRepositories_ShareSameTransaction()
    {
        var dbName = nameof(MultipleRepositories_ShareSameTransaction);
        var provider = CreateServiceProvider(dbName, out _);
        var orderRepo = provider.GetRequiredService<IRepository<Order, Guid>>();
        var productRepo = provider.GetRequiredService<IRepository<Product, Guid>>();

        var orderId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        await orderRepo.InsertAsync(new Order(orderId, "ORD-SHARED"));
        await productRepo.InsertAsync(new Product(productId, "Shared Product", 5));

        await CommitAsync(provider);

        Assert.NotNull(await orderRepo.GetAsync(orderId));
        Assert.NotNull(await productRepo.GetAsync(productId));
    }

    // ===================================================================
    //  Integration events 在事务提交后发布
    // ===================================================================

    [Fact]
    public async Task IntegrationEvents_ArePublishedAfterCommit()
    {
        var dbName = nameof(IntegrationEvents_ArePublishedAfterCommit);
        var provider = CreateServiceProvider(dbName, out var eventBus);
        var productRepo = provider.GetRequiredService<IRepository<Product, Guid>>();

        var productId = Guid.NewGuid();
        await productRepo.InsertAsync(new Product(productId, "Test", 10));
        await CommitAsync(provider);

        // 清除种子数据的事件
        eventBus.PublishedEvents.Clear();

        var product = await productRepo.GetAsync(productId);
        product.ReduceStock(3);
        await CommitAsync(provider);

        var integrationEvents = eventBus.PublishedEvents.Where(e => e is StockReducedIntegrationEvent).ToList();
        Assert.NotEmpty(integrationEvents);
    }

    // ===================================================================
    //  ExecuteCmd 测试（仅验证方法签名，InMemory 不支持 ExecuteSqlRaw）
    // ===================================================================

    [Fact]
    public async Task ExecuteCmd_WithInMemory_ThrowsExpectedException()
    {
        var dbName = nameof(ExecuteCmd_WithInMemory_ThrowsExpectedException);
        var provider = CreateServiceProvider(dbName, out _);
        var repo = provider.GetRequiredService<IRepository<Order, Guid>>();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repo.ExecuteCmd("UPDATE Orders SET OrderNumber = 'X'"));

        Assert.Contains("Relational-specific", ex.Message);
    }
}
