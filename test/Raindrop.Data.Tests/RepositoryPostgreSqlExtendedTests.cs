using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Raindrop.Data.EFCore;
using Raindrop.Data.Entities;
using Raindrop.Data.Events;
using Raindrop.Data.Repository;
using Raindrop.EventBus.Abstractions;
using Raindrop.EventBus.Core;
using Raindrop.EventBus.MediatR;
using Raindrop.UnitOfWork.Abstractions;

namespace Raindrop.Data.Tests;

[Collection(PostgreSqlCollection.Name)]
public class RepositoryPostgreSqlExtendedTests
{
    private readonly PostgreSqlTestFixture _postgres;
    public RepositoryPostgreSqlExtendedTests(PostgreSqlTestFixture postgres) { _postgres = postgres; }

    public sealed class Order : AggregateRoot<Guid>
    {
        public Order(Guid id, string orderNumber, Guid? productId = null) { Id = id; OrderNumber = orderNumber; ProductId = productId; }
        private Order() { OrderNumber = string.Empty; }
        public string OrderNumber { get; private set; }
        public OrderStatus Status { get; private set; } = OrderStatus.Pending;
        public Guid? ProductId { get; private set; }
        public void Complete() { Status = OrderStatus.Completed; AddDomainEvent(new OrderSubmittedEvent(Id, ProductId)); }
    }
    public enum OrderStatus { Pending, Completed }
    public sealed record OrderSubmittedEvent(Guid OrderId, Guid? ProductId) : IEvent;
    public sealed record StockReducedEvent(Guid ProductId, int Quantity) : IEvent;
    public sealed record StockReducedIntegrationEvent(Guid ProductId, int Quantity) : IIntegrationEvent;

    public sealed class Product : AggregateRoot<Guid>
    {
        public Product(Guid id, string name, int stock) { Id = id; Name = name; Stock = stock; }
        private Product() { Name = string.Empty; }
        public string Name { get; private set; }
        public int Stock { get; private set; }
        public void ReduceStock(int qty) { Stock -= qty; AddDomainEvent(new StockReducedEvent(Id, qty)); AddDomainEvent(new StockReducedIntegrationEvent(Id, qty)); }
    }
    public sealed class AuditLog : AggregateRoot<Guid>
    {
        public AuditLog(Guid id, string message) { Id = id; Message = message; }
        private AuditLog() { Message = string.Empty; }
        public string Message { get; private set; }
    }
    public sealed class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        public DbSet<Order> Orders => Set<Order>();
        public DbSet<Product> Products => Set<Product>();
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
        protected override void OnModelCreating(ModelBuilder mb)
        {
            var s = PostgreSqlTestFixture.SchemaName;
            mb.Entity<Order>(b => { b.ToTable("repo_orders3", s); b.HasKey(e => e.Id); b.Property(e => e.Id).HasColumnName("id"); b.Property(e => e.OrderNumber).HasColumnName("order_number").IsRequired().HasMaxLength(200); b.Property(e => e.Status).HasColumnName("status").HasConversion<int>(); b.Property(e => e.ProductId).HasColumnName("product_id"); });
            mb.Entity<Product>(b => { b.ToTable("repo_products3", s); b.HasKey(e => e.Id); b.Property(e => e.Id).HasColumnName("id"); b.Property(e => e.Name).HasColumnName("name").IsRequired().HasMaxLength(200); b.Property(e => e.Stock).HasColumnName("stock"); });
            mb.Entity<AuditLog>(b => { b.ToTable("repo_audit_logs3", s); b.HasKey(e => e.Id); b.Property(e => e.Id).HasColumnName("id"); b.Property(e => e.Message).HasColumnName("message").IsRequired().HasMaxLength(1000); });
        }
    }
    public sealed class OrderSubmittedHandler : IEventHandler<OrderSubmittedEvent>
    {
        private readonly IRepository<Product, Guid> _p;
        public OrderSubmittedHandler(IRepository<Product, Guid> p) { _p = p; }
        public async Task HandleAsync(OrderSubmittedEvent e, CancellationToken ct = default)
        { if (e.ProductId.HasValue) (await _p.GetAsync(e.ProductId.Value)).ReduceStock(1); }
    }
    public sealed class StockReducedHandler : IEventHandler<StockReducedEvent>
    {
        private readonly IRepository<AuditLog, Guid> _a;
        public StockReducedHandler(IRepository<AuditLog, Guid> a) { _a = a; }
        public async Task HandleAsync(StockReducedEvent e, CancellationToken ct = default)
        { await _a.InsertAsync(new AuditLog(Guid.NewGuid(), "Stock reduced")); }
    }
    public sealed class ThrowingOrderHandler : IEventHandler<OrderSubmittedEvent>
    {
        public Task HandleAsync(OrderSubmittedEvent e, CancellationToken ct = default) => throw new InvalidOperationException("Handler fail");
    }
    /// <summary>
    /// 测试用事件总线：领域事件在当前 scope 内直接调用 handler，集成事件模拟发布失败。
    /// </summary>
    public sealed class IntegrationEventFailingEventBus : IEventBus
    {
        private readonly IServiceProvider _services;
        public IntegrationEventFailingEventBus(IServiceProvider services) { _services = services; }

        /// <summary>集成事件失败的次数。</summary>
        public int FailedIntegrationEventCount { get; private set; }

        public async Task PublishAsync<TEvent>(TEvent eventData, CancellationToken ct = default) where TEvent : IEvent
        {
            if (eventData is IIntegrationEvent)
            {
                FailedIntegrationEventCount++;
                throw new InvalidOperationException("Integration event publish failure");
            }

            foreach (var handler in _services.GetServices<IEventHandler<TEvent>>())
            {
                await handler.HandleAsync(eventData, ct);
            }
        }
    }

    /// <summary>
    /// 测试用日志记录器，用来断言集成事件发布失败时会留下错误日志。
    /// </summary>
    public sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<CapturedLog> Logs { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Logs.Add(new CapturedLog(logLevel, formatter(state, exception), exception));
        }
    }

    public sealed record CapturedLog(LogLevel Level, string Message, Exception? Exception);

    private string Schema => PostgreSqlTestFixture.SchemaName;
    private async Task InitAsync()
    {
        await using var c = _postgres.CreateConnection(); await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = $@"
            create schema if not exists {Schema};
            create table if not exists {Schema}.repo_orders3 (id uuid primary key, order_number text not null, status integer not null default 0, product_id uuid);
            create table if not exists {Schema}.repo_products3 (id uuid primary key, name text not null, stock integer not null default 0);
            create table if not exists {Schema}.repo_audit_logs3 (id uuid primary key, message text not null);
            delete from {Schema}.repo_orders3; delete from {Schema}.repo_products3; delete from {Schema}.repo_audit_logs3;
            ";
        await cmd.ExecuteNonQueryAsync();
    }
    private static void AddMediatR(IServiceCollection s)
    {
        var asm = typeof(RepositoryPostgreSqlExtendedTests).Assembly;
        s.AddMediatR(cfg => { cfg.Lifetime = ServiceLifetime.Scoped; cfg.RegisterServicesFromAssembly(asm); });
        s.AddScoped<IEventTransport, MediatRLocalEventTransport>();
        s.AddEventBusCore<MediatREventRouteResolver>();
        var nh = typeof(INotificationHandler<>).MakeGenericType(typeof(MediatREventNotification<>).MakeGenericType(typeof(OrderSubmittedEvent)));
        var ad = typeof(MediatREventNotificationHandler<>).MakeGenericType(typeof(OrderSubmittedEvent));
        s.TryAddEnumerable(ServiceDescriptor.Transient(nh, ad));
    }

    private static async Task CommitAsync(IServiceProvider provider)
    {
        var uow = await provider.GetRequiredService<IUnitOfWorkFactory>().BeginAsync();
        await uow.CommitAsync();
    }

    [Fact] public async Task AddRaindropData_WithPostgreSql_RegistersRepositoriesAndUnitOfWork()
    {
        await InitAsync();
        var s = new ServiceCollection();
        s.AddRaindropData<TestDbContext>(b => b.UseNpgsql(_postgres.ConnectionString + $";SearchPath={Schema}", o => o.SetPostgresVersion(14, 0)));
        await using var sp = s.BuildServiceProvider();
        var repo = sp.GetRequiredService<IRepository<Order, Guid>>();
        var id = Guid.NewGuid();
        await repo.InsertAsync(new Order(id, "PUBLIC-API")); await CommitAsync(sp);
        Assert.Equal("PUBLIC-API", (await repo.GetAsync(id)).OrderNumber);
    }

    [Fact] public async Task CommitAsync_WithoutBegin_ThrowsForRelationalProvider()
    {
        await InitAsync();
        var services = new ServiceCollection();
        services.AddRaindropData<TestDbContext>(b => b.UseNpgsql(_postgres.ConnectionString + $";SearchPath={Schema}", o => o.SetPostgresVersion(14, 0)));

        await using var sp = services.BuildServiceProvider();
        var repo = sp.GetRequiredService<IRepository<Order, Guid>>();
        await repo.InsertAsync(new Order(Guid.NewGuid(), "NO-BEGIN"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.UnitOfWork.CommitAsync());
    }

    [Fact] public async Task UnitOfWorkFactory_BeginsTransactionSharedByRepositoryAndAdoNet()
    {
        await InitAsync();
        var services = new ServiceCollection();
        services.AddRaindropData<TestDbContext>(b => b.UseNpgsql(_postgres.ConnectionString + $";SearchPath={Schema}", o => o.SetPostgresVersion(14, 0)));

        await using var sp = services.BuildServiceProvider();
        var repo = sp.GetRequiredService<IRepository<Order, Guid>>();
        await using var uow = await sp.GetRequiredService<IUnitOfWorkFactory>().BeginAsync();
        var relational = Assert.IsAssignableFrom<IRelationalUnitOfWork>(uow);

        var adoOrderId = Guid.NewGuid();
        await using (var command = relational.Connection.CreateCommand())
        {
            command.Transaction = relational.Transaction;
            command.CommandText = "insert into repo_orders3 (id, order_number, status) values (@id, @orderNumber, @status)";

            var idParameter = command.CreateParameter();
            idParameter.ParameterName = "id";
            idParameter.Value = adoOrderId;
            command.Parameters.Add(idParameter);

            var orderNumberParameter = command.CreateParameter();
            orderNumberParameter.ParameterName = "orderNumber";
            orderNumberParameter.Value = "ADO-SHARED";
            command.Parameters.Add(orderNumberParameter);

            var statusParameter = command.CreateParameter();
            statusParameter.ParameterName = "status";
            statusParameter.Value = 0;
            command.Parameters.Add(statusParameter);

            await command.ExecuteNonQueryAsync();
        }

        var repoOrderId = Guid.NewGuid();
        await repo.InsertAsync(new Order(repoOrderId, "REPO-SHARED"));

        await uow.CommitAsync();

        Assert.Equal("ADO-SHARED", (await repo.GetAsync(adoOrderId))!.OrderNumber);
        Assert.Equal("REPO-SHARED", (await repo.GetAsync(repoOrderId))!.OrderNumber);
    }

    [Fact] public async Task SaveChangesFailure_RollsBackAllChanges()
    {
        await InitAsync();
        var services = new ServiceCollection();
        services.AddRaindropData<TestDbContext>(b => b.UseNpgsql(_postgres.ConnectionString + $";SearchPath={Schema}", o => o.SetPostgresVersion(14, 0)));
        await using var sp = services.BuildServiceProvider();
        var oid = Guid.NewGuid(); var pid = Guid.NewGuid();
        await sp.GetRequiredService<IRepository<Order, Guid>>().InsertAsync(new Order(oid, "ATOMIC"));
        await sp.GetRequiredService<IRepository<Product, Guid>>().InsertAsync(new Product(pid, null!, 10));
        await Assert.ThrowsAsync<DbUpdateException>(() => CommitAsync(sp));
        await using var sp2 = services.BuildServiceProvider();
        Assert.Null(await sp2.GetRequiredService<IRepository<Order, Guid>>().GetAsync(oid));
    }

    [Fact] public async Task ExecuteCmd_ThenRollback_DoesNotRollbackSqlCommand()
    {
        await InitAsync();
        var services = new ServiceCollection();
        services.AddRaindropData<TestDbContext>(b => b.UseNpgsql(_postgres.ConnectionString + $";SearchPath={Schema}", o => o.SetPostgresVersion(14, 0)));
        var id = Guid.NewGuid();
        await using (var sp = services.BuildServiceProvider()) { var r = sp.GetRequiredService<IRepository<Order, Guid>>(); await r.InsertAsync(new Order(id, "EXEC")); await CommitAsync(sp); }
        await using (var sp = services.BuildServiceProvider()) { var r = sp.GetRequiredService<IRepository<Order, Guid>>(); await r.ExecuteCmd("UPDATE repo_orders3 SET order_number = 'BYPASS' WHERE id = @p0", id); await r.UnitOfWork.RollbackAsync(); }
        await using (var sp = services.BuildServiceProvider()) { Assert.Equal("BYPASS", (await sp.GetRequiredService<IRepository<Order, Guid>>().GetAsync(id)).OrderNumber); }
    }

    [Fact] public async Task OnCompleted_WhenThrows_DataStillCommitted_AndCommitDoesNotThrow()
    {
        await InitAsync();
        var services = new ServiceCollection();
        services.AddRaindropData<TestDbContext>(b => b.UseNpgsql(_postgres.ConnectionString + $";SearchPath={Schema}", o => o.SetPostgresVersion(14, 0)));
        var id = Guid.NewGuid();
        await using (var sp = services.BuildServiceProvider())
        {
            var r = sp.GetRequiredService<IRepository<Order, Guid>>();
            r.UnitOfWork.OnCompleted(async () => throw new InvalidOperationException("OnCompleted fail"));
            await r.InsertAsync(new Order(id, "ONCOMP"));
            // OnCompleted 失败不应导致 CommitAsync 抛异常
                await CommitAsync(sp);
        }
        await using (var sp = services.BuildServiceProvider())
            Assert.NotNull(await sp.GetRequiredService<IRepository<Order, Guid>>().GetAsync(id));
    }

    [Fact] public async Task AddRaindropData_AutoScansAllAggregateRoots()
    {
        await InitAsync();
        var s = new ServiceCollection();
        s.AddRaindropData<TestDbContext>(b => b.UseNpgsql(_postgres.ConnectionString + $";SearchPath={Schema}", o => o.SetPostgresVersion(14, 0)));
        await using var sp = s.BuildServiceProvider();
        var oid = Guid.NewGuid(); var pid = Guid.NewGuid();
        await sp.GetRequiredService<IRepository<Order, Guid>>().InsertAsync(new Order(oid, "SCAN", pid));
        await sp.GetRequiredService<IRepository<Product, Guid>>().InsertAsync(new Product(pid, "ScanItem", 5));
        await sp.GetRequiredService<IRepository<AuditLog, Guid>>().InsertAsync(new AuditLog(Guid.NewGuid(), "Scan"));
        await CommitAsync(sp);
        Assert.Equal("SCAN", (await sp.GetRequiredService<IRepository<Order, Guid>>().GetAsync(oid)).OrderNumber);
    }

    [Fact] public async Task DomainEventHandlerException_PreventsSaveChanges()
    {
        await InitAsync();
        var pid = Guid.NewGuid(); var oid = Guid.NewGuid();
        var baseS = new ServiceCollection();
        baseS.AddRaindropData<TestDbContext>(b => b.UseNpgsql(_postgres.ConnectionString + $";SearchPath={Schema}", o => o.SetPostgresVersion(14, 0)));
        baseS.AddScoped<IEventHandler<OrderSubmittedEvent>, ThrowingOrderHandler>();
        AddMediatR(baseS);
        await using (var sp = baseS.BuildServiceProvider())
        {
            await sp.GetRequiredService<IRepository<Product, Guid>>().InsertAsync(new Product(pid, "Item", 5));
            await sp.GetRequiredService<IRepository<Order, Guid>>().InsertAsync(new Order(oid, "PREVENT", pid));
            await CommitAsync(sp);
        }
        await using (var sp = baseS.BuildServiceProvider())
        {
            (await sp.GetRequiredService<IRepository<Order, Guid>>().GetAsync(oid)).Complete();
            await Assert.ThrowsAsync<InvalidOperationException>(() => CommitAsync(sp));
        }
        var vS = new ServiceCollection();
        vS.AddRaindropData<TestDbContext>(b => b.UseNpgsql(_postgres.ConnectionString + $";SearchPath={Schema}", o => o.SetPostgresVersion(14, 0)));
        await using var vsp = vS.BuildServiceProvider();
        Assert.Equal(OrderStatus.Pending, (await vsp.GetRequiredService<IRepository<Order, Guid>>().GetAsync(oid)).Status);
    }

        // ===================================================================
        //  Integration event failure does not rollback committed data
        // ===================================================================

        [Fact]
        public async Task IntegrationEventFailure_DoesNotRollbackCommittedData()
        {
            await InitAsync();
            var pid = Guid.NewGuid(); var oid = Guid.NewGuid();

            var services = new ServiceCollection();
            services.AddRaindropData<TestDbContext>(b => b.UseNpgsql(_postgres.ConnectionString + $";SearchPath={Schema}", o => o.SetPostgresVersion(14, 0)));
            services.AddScoped<IEventHandler<OrderSubmittedEvent>, OrderSubmittedHandler>();
            services.AddScoped<IEventHandler<StockReducedEvent>, StockReducedHandler>();
            services.AddScoped<IEventBus, IntegrationEventFailingEventBus>();

            await using var provider = services.BuildServiceProvider();
            var ir = provider.GetRequiredService<IRepository<Order, Guid>>();
            var pr = provider.GetRequiredService<IRepository<Product, Guid>>();
            await pr.InsertAsync(new Product(pid, "Pen", 10));
            await ir.InsertAsync(new Order(oid, "INTEG-TEST", pid));
            await CommitAsync(provider);

            // 触发领域事件：OrderSubmitted → handler 扣减库存 → StockReduced + StockReducedIntegrationEvent
            var r = provider.GetRequiredService<IRepository<Order, Guid>>();
            (await r.GetAsync(oid)).Complete();
            await CommitAsync(provider);

            // 验证集成事件确实因为 throw 而失败了
            var failingBus = (IntegrationEventFailingEventBus)provider.GetRequiredService<IEventBus>();
            Assert.True(failingBus.FailedIntegrationEventCount > 0,
                "集成事件应该至少有一次发布失败");

            // 验证数据仍然提交了（集成事件失败不回滚已提交事务）
            var vr = provider.GetRequiredService<IRepository<Order, Guid>>();
            var vp = provider.GetRequiredService<IRepository<Product, Guid>>();
            Assert.Equal(OrderStatus.Completed, (await vr.GetAsync(oid)).Status);
            Assert.Equal(9, (await vp.GetAsync(pid)).Stock);
            Assert.NotEmpty(await provider.GetRequiredService<IRepository<AuditLog, Guid>>().GetAllAsync());
        }

        [Fact]
        public async Task IntegrationEventFailure_WritesErrorLog()
        {
            await InitAsync();
            var pid = Guid.NewGuid(); var oid = Guid.NewGuid();
            var logger = new CapturingLogger<DomainEventProcessor>();

            var services = new ServiceCollection();
            services.AddRaindropData<TestDbContext>(b => b.UseNpgsql(_postgres.ConnectionString + $";SearchPath={Schema}", o => o.SetPostgresVersion(14, 0)));
            services.AddScoped<IEventHandler<OrderSubmittedEvent>, OrderSubmittedHandler>();
            services.AddScoped<IEventHandler<StockReducedEvent>, StockReducedHandler>();
            services.AddScoped<IEventBus, IntegrationEventFailingEventBus>();
            services.AddSingleton<ILogger<DomainEventProcessor>>(logger);

            await using var provider = services.BuildServiceProvider();
            var ir = provider.GetRequiredService<IRepository<Order, Guid>>();
            var pr = provider.GetRequiredService<IRepository<Product, Guid>>();
            await pr.InsertAsync(new Product(pid, "Pen", 10));
            await ir.InsertAsync(new Order(oid, "INTEG-LOG", pid));
            await CommitAsync(provider);

            (await ir.GetAsync(oid)).Complete();
            await CommitAsync(provider);

            Assert.Contains(logger.Logs, log =>
                log.Level == LogLevel.Error &&
                log.Exception is InvalidOperationException &&
                log.Message.Contains(nameof(StockReducedIntegrationEvent), StringComparison.Ordinal));
        }
    }

