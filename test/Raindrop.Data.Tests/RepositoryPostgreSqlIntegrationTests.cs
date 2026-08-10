using System.Linq.Expressions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Raindrop.Data.EFCore;
using Raindrop.Data.Entities;
using Raindrop.Data.Events;
using Raindrop.Data.Repository;
using Raindrop.Data.Specifications;
using Raindrop.EventBus.Abstractions;
using Raindrop.EventBus.Core;
using Raindrop.EventBus.MediatR;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Raindrop.UnitOfWork.Abstractions;

namespace Raindrop.Data.Tests;

[Collection(PostgreSqlCollection.Name)]
public class RepositoryPostgreSqlIntegrationTests
{
    private readonly PostgreSqlTestFixture _postgres;

    public RepositoryPostgreSqlIntegrationTests(PostgreSqlTestFixture postgres)
    {
        _postgres = postgres;
    }

    // ===== Domain models =====

    public sealed class Order : AggregateRoot<Guid>
    {
        public Order(Guid id, string orderNumber, Guid? productId = null) { Id = id; OrderNumber = orderNumber; ProductId = productId; }
        private Order() { OrderNumber = string.Empty; }
        public string OrderNumber { get; private set; }
        public OrderStatus Status { get; private set; } = OrderStatus.Pending;
        public Guid? ProductId { get; private set; }
        public void Complete() { Status = OrderStatus.Completed; AddDomainEvent(new OrderSubmittedEvent(Id, ProductId)); }
    }
    public enum OrderStatus { Pending, Completed, Cancelled }
    public sealed record OrderSubmittedEvent(Guid OrderId, Guid? ProductId) : IEvent;
    public sealed record StockReducedEvent(Guid ProductId, int Quantity) : IEvent;
    public sealed record StockReducedIntegrationEvent(Guid ProductId, int Quantity) : IIntegrationEvent;

    public sealed class Product : AggregateRoot<Guid>
    {
        public Product(Guid id, string name, int stock) { Id = id; Name = name; Stock = stock; }
        private Product() { Name = string.Empty; }
        public string Name { get; private set; }
        public int Stock { get; private set; }
        public void ReduceStock(int quantity) { Stock -= quantity; AddDomainEvent(new StockReducedEvent(Id, quantity)); AddDomainEvent(new StockReducedIntegrationEvent(Id, quantity)); }
    }

    public sealed class AuditLog : AggregateRoot<Guid>
    {
        public AuditLog(Guid id, string message) { Id = id; Message = message; }
        private AuditLog() { Message = string.Empty; }
        public string Message { get; private set; }
    }

    public sealed class AutoIdEntity : AggregateRoot<int>
    {
        public AutoIdEntity(string label) { Label = label; }
        private AutoIdEntity() { Label = string.Empty; }
        public string Label { get; private set; }
    }

    // ===== Event handlers =====

    public sealed class OrderSubmittedHandler : IEventHandler<OrderSubmittedEvent>
    {
        private readonly IRepository<Product, Guid> _productRepo;
        public OrderSubmittedHandler(IRepository<Product, Guid> productRepo) { _productRepo = productRepo; }
        public async Task HandleAsync(OrderSubmittedEvent eventData, CancellationToken ct = default)
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
        public StockReducedHandler(IRepository<AuditLog, Guid> auditLogRepo) { _auditLogRepo = auditLogRepo; }
        public async Task HandleAsync(StockReducedEvent eventData, CancellationToken ct = default)
        {
            await _auditLogRepo.InsertAsync(new AuditLog(Guid.NewGuid(), "Stock reduced"));
        }
    }

    public sealed class ThrowingOrderSubmittedHandler : IEventHandler<OrderSubmittedEvent>
    {
        public Task HandleAsync(OrderSubmittedEvent eventData, CancellationToken ct = default)
            => throw new InvalidOperationException("Simulated handler exception");
    }

    // ===== DbContext =====

    public sealed class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        public string Schema => PostgreSqlTestFixture.SchemaName;
        public DbSet<Order> Orders => Set<Order>();
        public DbSet<Product> Products => Set<Product>();
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
        public DbSet<AutoIdEntity> AutoIdEntities => Set<AutoIdEntity>();

        protected override void OnModelCreating(ModelBuilder mb)
        {
            mb.Entity<Order>(b => {
                b.ToTable("repo_orders", Schema);
                b.HasKey(e => e.Id);
                b.Property(e => e.Id).HasColumnName("id");
                b.Property(e => e.OrderNumber).HasColumnName("order_number").IsRequired().HasMaxLength(200);
                b.Property(e => e.Status).HasColumnName("status").HasConversion<int>();
                b.Property(e => e.ProductId).HasColumnName("product_id");
            });
            mb.Entity<Product>(b => {
                b.ToTable("repo_products", Schema);
                b.HasKey(e => e.Id);
                b.Property(e => e.Id).HasColumnName("id");
                b.Property(e => e.Name).HasColumnName("name").IsRequired().HasMaxLength(200);
                b.Property(e => e.Stock).HasColumnName("stock");
            });
            mb.Entity<AuditLog>(b => {
                b.ToTable("repo_audit_logs", Schema);
                b.HasKey(e => e.Id);
                b.Property(e => e.Id).HasColumnName("id");
                b.Property(e => e.Message).HasColumnName("message").IsRequired();
            });
            mb.Entity<AutoIdEntity>(b => {
                b.ToTable("repo_auto_id_entities", Schema);
                b.HasKey(e => e.Id);
                b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
                b.Property(e => e.Label).HasColumnName("label").IsRequired().HasMaxLength(200);
            });
        }
    }

    // ===== Schema setup =====

    private string Schema => PostgreSqlTestFixture.SchemaName;

    private async Task InitSchemaAsync()
    {
        await using var conn = _postgres.CreateConnection();
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            create schema if not exists {Schema};

            create table if not exists {Schema}.repo_orders (
                id uuid primary key, order_number text not null,
                status integer not null default 0, product_id uuid);

            create table if not exists {Schema}.repo_products (
                id uuid primary key, name text not null, stock integer not null default 0);

            create table if not exists {Schema}.repo_audit_logs (
                id uuid primary key, message text not null);

            create table if not exists {Schema}.repo_auto_id_entities (
                id integer generated always as identity primary key, label text not null);

            delete from {Schema}.repo_orders;
            delete from {Schema}.repo_products;
            delete from {Schema}.repo_audit_logs;
            delete from {Schema}.repo_auto_id_entities;
        ";
        await cmd.ExecuteNonQueryAsync();
    }

    // ===== DI factories =====

    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();

        services.AddDbContext<TestDbContext>(b => b.UseNpgsql(
            _postgres.ConnectionString + ";SearchPath=" + PostgreSqlTestFixture.SchemaName, o => o.SetPostgresVersion(14, 0)));

        services.AddScoped(sp =>
        {
            var ctx = sp.GetRequiredService<TestDbContext>();
            return new EntityFrameworkCoreUnitOfWork<TestDbContext>(ctx, sp.GetService<IEventBus>());
        });
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<EntityFrameworkCoreUnitOfWork<TestDbContext>>());
        services.AddScoped<IUnitOfWorkFactory, EntityFrameworkCoreUnitOfWorkFactory<TestDbContext>>();
        services.AddScoped<IRepository<Order, Guid>, EntityFrameworkCoreRepository<TestDbContext, Order, Guid>>();
        services.AddScoped<IRepository<Product, Guid>, EntityFrameworkCoreRepository<TestDbContext, Product, Guid>>();
        services.AddScoped<IRepository<AuditLog, Guid>, EntityFrameworkCoreRepository<TestDbContext, AuditLog, Guid>>();
        services.AddScoped<IRepository<AutoIdEntity, int>, EntityFrameworkCoreRepository<TestDbContext, AutoIdEntity, int>>();

        return services.BuildServiceProvider();
    }

    private ServiceProvider BuildMediatRProvider(Action<IServiceCollection>? extra = null)
    {
        var services = new ServiceCollection();

        services.AddDbContext<TestDbContext>(b => b.UseNpgsql(
            _postgres.ConnectionString + ";SearchPath=" + PostgreSqlTestFixture.SchemaName, o => o.SetPostgresVersion(14, 0)));
        services.AddScoped(sp =>
        {
            var ctx = sp.GetRequiredService<TestDbContext>();
            return new EntityFrameworkCoreUnitOfWork<TestDbContext>(ctx, sp.GetService<IEventBus>());
        });
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<EntityFrameworkCoreUnitOfWork<TestDbContext>>());
        services.AddScoped<IUnitOfWorkFactory, EntityFrameworkCoreUnitOfWorkFactory<TestDbContext>>();
        services.AddScoped<IRepository<Order, Guid>, EntityFrameworkCoreRepository<TestDbContext, Order, Guid>>();
        services.AddScoped<IRepository<Product, Guid>, EntityFrameworkCoreRepository<TestDbContext, Product, Guid>>();
        services.AddScoped<IRepository<AuditLog, Guid>, EntityFrameworkCoreRepository<TestDbContext, AuditLog, Guid>>();

        services.AddScoped<IEventHandler<OrderSubmittedEvent>, OrderSubmittedHandler>();
        services.AddScoped<IEventHandler<StockReducedEvent>, StockReducedHandler>();
        extra?.Invoke(services);

        // Manual MediatR pipeline (no assembly scan to avoid picking up ThrowingOrderSubmittedHandler)
        var executingAssembly = typeof(RepositoryPostgreSqlIntegrationTests).Assembly;
        services.AddMediatR(cfg =>
        {
            cfg.Lifetime = ServiceLifetime.Scoped;
            cfg.RegisterServicesFromAssembly(executingAssembly);
        });
        services.AddScoped<IEventTransport, MediatRLocalEventTransport>();
        services.AddEventBusCore<MediatREventRouteResolver>();

        // Register MediatR notification handler adapters for the two events we use
        // We build adapters manually instead of scanning, to avoid picking up ThrowingOrderSubmittedHandler
        RegisterHandlerAdapter<OrderSubmittedEvent>(services);
        RegisterHandlerAdapter<StockReducedEvent>(services);

        return services.BuildServiceProvider();
    }

    private static void RegisterHandlerAdapter<TEvent>(IServiceCollection services)
        where TEvent : IEvent
    {
        var notificationHandlerType = typeof(INotificationHandler<>).MakeGenericType(
            typeof(MediatREventNotification<>).MakeGenericType(typeof(TEvent)));
        var adapterType = typeof(MediatREventNotificationHandler<>).MakeGenericType(typeof(TEvent));
        services.TryAddEnumerable(ServiceDescriptor.Transient(notificationHandlerType, adapterType));
    }

    private static async Task CommitAsync(IServiceProvider provider)
    {
        var uow = await provider.GetRequiredService<IUnitOfWorkFactory>().BeginAsync();
        await uow.CommitAsync();
    }

    // ===== Tests =====

    [Fact]
    public async Task InsertAsync_Then_GetAsync_ReturnsEntity()
    {
        await InitSchemaAsync();
        await using var p = BuildProvider();
        var repo = p.GetRequiredService<IRepository<Order, Guid>>();
        var id = Guid.NewGuid();
        await repo.InsertAsync(new Order(id, "PG-ORD-001"));
        await CommitAsync(p);
        Assert.Equal("PG-ORD-001", (await repo.GetAsync(id)).OrderNumber);
    }

    [Fact]
    public async Task UpdateAsync_PersistsChanges()
    {
        await InitSchemaAsync();
        await using var p = BuildProvider();
        var repo = p.GetRequiredService<IRepository<Order, Guid>>();
        var id = Guid.NewGuid();
        await repo.InsertAsync(new Order(id, "PG-UPDATE"));
        await CommitAsync(p);
        var order = await repo.GetAsync(id);
        order.Complete();
        await repo.UpdateAsync(order);
        await CommitAsync(p);
        await using var p2 = BuildProvider();
        Assert.Equal(OrderStatus.Completed, (await p2.GetRequiredService<IRepository<Order, Guid>>().GetAsync(id)).Status);
    }

    [Fact]
    public async Task DeleteAsync_RemovesEntity()
    {
        await InitSchemaAsync();
        await using var p = BuildProvider();
        var repo = p.GetRequiredService<IRepository<Order, Guid>>();
        var id = Guid.NewGuid();
        await repo.InsertAsync(new Order(id, "PG-DEL"));
        await CommitAsync(p);
        await repo.DeleteAsync(await repo.GetAsync(id));
        await CommitAsync(p);
        Assert.Null(await repo.GetAsync(id));
    }

    [Fact]
    public async Task InsertAndGetIdAsync_WithAutoIncrement_ReturnsId()
    {
        await InitSchemaAsync();
        await using var p = BuildProvider();
        var repo = p.GetRequiredService<IRepository<AutoIdEntity, int>>();
        var id = await repo.InsertAndGetIdAsync(new AutoIdEntity("Auto"));
        Assert.True(id > 0);
        Assert.Equal("Auto", (await repo.GetAsync(id)).Label);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsAll()
    {
        await InitSchemaAsync();
        await using var p = BuildProvider();
        var repo = p.GetRequiredService<IRepository<Order, Guid>>();
        await repo.InsertAsync(new Order(Guid.NewGuid(), "PG-A"));
        await repo.InsertAsync(new Order(Guid.NewGuid(), "PG-B"));
        await CommitAsync(p);
        Assert.Equal(2, (await repo.GetAllAsync()).Count);
    }

    [Fact]
    public async Task QueryAsync_FiltersByPredicate()
    {
        await InitSchemaAsync();
        await using var p = BuildProvider();
        var repo = p.GetRequiredService<IRepository<Order, Guid>>();
        await repo.InsertAsync(new Order(Guid.NewGuid(), "PG-QA"));
        await repo.InsertAsync(new Order(Guid.NewGuid(), "PG-QB"));
        await CommitAsync(p);
        Assert.Single(await repo.QueryAsync<Order>(o => o.OrderNumber == "PG-QA"));
    }

    [Fact]
    public async Task FirstOrDefaultAsync_ReturnsMatching()
    {
        await InitSchemaAsync();
        await using var p = BuildProvider();
        var repo = p.GetRequiredService<IRepository<Order, Guid>>();
        await repo.InsertAsync(new Order(Guid.NewGuid(), "PG-FIRST"));
        await CommitAsync(p);
        Assert.NotNull(await repo.FirstOrDefaultAsync<Order>(o => o.OrderNumber == "PG-FIRST"));
    }

    private sealed class CompletedOrdersSpec : BaseSpecification<Order>
    {
        public CompletedOrdersSpec() : base(o => o.Status == OrderStatus.Completed) { ApplyOrderByDescending(o => o.OrderNumber); }
    }

    [Fact]
    public async Task GetListAsync_WithSpecification_FiltersAndOrders()
    {
        await InitSchemaAsync();
        await using var p = BuildProvider();
        var repo = p.GetRequiredService<IRepository<Order, Guid>>();
        var o1 = new Order(Guid.NewGuid(), "PG-C"); o1.Complete();
        var o2 = new Order(Guid.NewGuid(), "PG-A"); o2.Complete();
        var o3 = new Order(Guid.NewGuid(), "PG-B");
        await repo.InsertAsync(o1); await repo.InsertAsync(o2); await repo.InsertAsync(o3);
        await CommitAsync(p);
        var r = await repo.GetListAsync(new CompletedOrdersSpec());
        Assert.Equal(2, r.Count);
        Assert.Equal("PG-C", r[0].OrderNumber);
        Assert.Equal("PG-A", r[1].OrderNumber);
    }

    [Fact]
    public async Task ExecuteCmd_UpdatesData()
    {
        await InitSchemaAsync();
        var id = Guid.NewGuid();

        await using (var p = BuildProvider())
        {
            var repo = p.GetRequiredService<IRepository<Order, Guid>>();
            await repo.InsertAsync(new Order(id, "PG-SQL"));
            await CommitAsync(p);
        }

        await using (var p = BuildProvider())
        {
            var repo = p.GetRequiredService<IRepository<Order, Guid>>();
            await repo.ExecuteCmd("UPDATE repo_orders SET order_number = 'PG-SQL-UPDATED' WHERE id = @p0", id);
        }

        await using (var p = BuildProvider())
        {
            var repo = p.GetRequiredService<IRepository<Order, Guid>>();
            Assert.Equal("PG-SQL-UPDATED", (await repo.GetAsync(id)).OrderNumber);
        }
    }

    [Fact]
    public async Task RecursiveDomainEvents_AllChangesInOneTransaction()
    {
        await InitSchemaAsync();
        await using var p = BuildMediatRProvider();
        var orderRepo = p.GetRequiredService<IRepository<Order, Guid>>();
        var productId = Guid.NewGuid(); var orderId = Guid.NewGuid();
        await p.GetRequiredService<IRepository<Product, Guid>>().InsertAsync(new Product(productId, "Pen", 20));
        await orderRepo.InsertAsync(new Order(orderId, "PG-RECURSE", productId));
        await CommitAsync(p);
        (await orderRepo.GetAsync(orderId)).Complete();
        await CommitAsync(p);
        Assert.Equal(19, (await p.GetRequiredService<IRepository<Product, Guid>>().GetAsync(productId)).Stock);
        Assert.NotEmpty(await p.GetRequiredService<IRepository<AuditLog, Guid>>().GetAllAsync());
    }

    [Fact]
    public async Task DomainEventHandlerException_PreventsSaveChanges()
    {
        await InitSchemaAsync();
        var productId = Guid.NewGuid(); var orderId = Guid.NewGuid();
        await using (var init = BuildMediatRProvider())
        {
            var pr = init.GetRequiredService<IRepository<Product, Guid>>();
            var ir = init.GetRequiredService<IRepository<Order, Guid>>();
            await pr.InsertAsync(new Product(productId, "RBItem", 5));
            await ir.InsertAsync(new Order(orderId, "PG-RB-TX", productId));
            await CommitAsync(init);
        }
        await using (var p = BuildMediatRProvider(s => s.AddScoped<IEventHandler<OrderSubmittedEvent>, ThrowingOrderSubmittedHandler>()))
        {
            (await p.GetRequiredService<IRepository<Order, Guid>>().GetAsync(orderId)).Complete();
            await Assert.ThrowsAsync<InvalidOperationException>(() => CommitAsync(p));
        }
        await using var vp = BuildMediatRProvider();
        Assert.Equal(OrderStatus.Pending, (await vp.GetRequiredService<IRepository<Order, Guid>>().GetAsync(orderId)).Status);
        Assert.Equal(5, (await vp.GetRequiredService<IRepository<Product, Guid>>().GetAsync(productId)).Stock);
    }

    [Fact]
    public async Task RollbackAsync_DiscardsChanges()
    {
        await InitSchemaAsync();
        await using var p = BuildProvider();
        var repo = p.GetRequiredService<IRepository<Order, Guid>>();
        var id = Guid.NewGuid();
        await repo.InsertAsync(new Order(id, "PG-RB"));
        await repo.UnitOfWork.RollbackAsync();
        Assert.Null(await repo.GetAsync(id));
    }

    [Fact]
    public async Task OnCompleted_RunsAfterCommit()
    {
        await InitSchemaAsync();
        await using var p = BuildProvider();
        var repo = p.GetRequiredService<IRepository<Order, Guid>>();
        var ok = false;
        repo.UnitOfWork.OnCompleted(async () => { ok = true; await Task.CompletedTask; });
        await repo.InsertAsync(new Order(Guid.NewGuid(), "PG-OC"));
        await CommitAsync(p);
        Assert.True(ok);
    }

    [Fact]
    public async Task OnCompleted_SkippedOnRollback()
    {
        await InitSchemaAsync();
        await using var p = BuildProvider();
        var repo = p.GetRequiredService<IRepository<Order, Guid>>();
        var ok = false;
        repo.UnitOfWork.OnCompleted(async () => { ok = true; await Task.CompletedTask; });
        await repo.InsertAsync(new Order(Guid.NewGuid(), "PG-OC-RB"));
        await repo.UnitOfWork.RollbackAsync();
        Assert.False(ok);
    }

    [Fact]
    public async Task MultipleRepositories_SingleTransaction()
    {
        await InitSchemaAsync();
        await using var p = BuildProvider();
        var oid = Guid.NewGuid(); var pid = Guid.NewGuid();
        await p.GetRequiredService<IRepository<Order, Guid>>().InsertAsync(new Order(oid, "PG-MULTI"));
        await p.GetRequiredService<IRepository<Product, Guid>>().InsertAsync(new Product(pid, "Multi", 50));
        await CommitAsync(p);
        Assert.NotNull(await p.GetRequiredService<IRepository<Order, Guid>>().GetAsync(oid));
        Assert.NotNull(await p.GetRequiredService<IRepository<Product, Guid>>().GetAsync(pid));
    }

}
