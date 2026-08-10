using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Raindrop.Data.Entities;
using Raindrop.Data.Events;
using Raindrop.Data.Repository;
using Raindrop.UnitOfWork.Abstractions;

namespace Raindrop.AppConfiguration.Sample.Scenarios;

/// <summary>
/// 演示仓储 + 工作单元：
///   - 聚合根定义（Order、Product、AuditLog）
///   - IRepository CRUD 操作
///   - IUnitOfWorkFactory.BeginAsync / CommitAsync / RollbackAsync
///   - 领域事件在 CommitAsync 中递归处理（需要注册 IEventBus 才能触发）
///   - 同一 scope 内多次 Begin/Commit
/// </summary>
public static class DataSampleScenario
{
    private const string ConnectionString =
        "Host=localhost;Port=5432;Username=postgres;Password=123456;Database=postgres";

    private const string SchemaName = "raindrop_sample_data";

    public static async Task RunAsync()
    {
        Console.WriteLine("===== 仓储 + 工作单元示例 =====");

        await PrepareDatabaseAsync();

        var services = new ServiceCollection();
        services.AddLogging();

        // 一步注册：DbContext + 工作单元 + 所有聚合根仓储（自动扫描）
        services.AddRaindropData<SampleDbContext>(options =>
            options.UseNpgsql(ConnectionString + $";SearchPath={SchemaName}"));

        await using var provider = services.BuildServiceProvider();

        // ── 插入 ──
        var productId = Guid.NewGuid();
        var productRepo = provider.GetRequiredService<IRepository<Product, Guid>>();
        var uow1 = await provider.GetRequiredService<IUnitOfWorkFactory>().BeginAsync();
        await productRepo.InsertAsync(new Product(productId, "键盘", 10));
        await uow1.CommitAsync();
        Console.WriteLine($"  插入：键盘 (id={productId})，库存 10");
        Console.WriteLine($"  CommitAsync 成功");

        // ── 查询 ──
        var loaded = await productRepo.GetAsync(productId);
        Console.WriteLine($"  查询：{loaded!.Name}，库存 {loaded.Stock}");

        // ── 更新 ──
        var uow2 = await provider.GetRequiredService<IUnitOfWorkFactory>().BeginAsync();
        loaded.ReduceStock(3);
        await productRepo.UpdateAsync(loaded);
        await uow2.CommitAsync();
        Console.WriteLine($"  更新：库存 {loaded.Stock}（扣减 3）");

        // ── 查询所有 ──
        var orderRepo = provider.GetRequiredService<IRepository<Order, Guid>>();
        var orderId1 = Guid.NewGuid();
        var orderId2 = Guid.NewGuid();
        var uow3 = await provider.GetRequiredService<IUnitOfWorkFactory>().BeginAsync();
        await orderRepo.InsertAsync(new Order(orderId1, "ORD-001"));
        await orderRepo.InsertAsync(new Order(orderId2, "ORD-002"));
        await uow3.CommitAsync();

        var allOrders = await orderRepo.GetAllAsync();
        Console.WriteLine($"  查询所有订单：{allOrders.Count} 条");

        // ── 按条件查询 ──
        var filtered = await orderRepo.QueryAsync<Order>(o => o.OrderNumber == "ORD-001");
        Console.WriteLine($"  条件查询：{filtered.Count} 条匹配");

        // ── 删除 ──
        var uow4 = await provider.GetRequiredService<IUnitOfWorkFactory>().BeginAsync();
        await orderRepo.DeleteAsync(allOrders[0]);
        await uow4.CommitAsync();
        Console.WriteLine($"  删除后剩余：{(await orderRepo.GetAllAsync()).Count} 条");

        // ── 回滚 ──
        var uow5 = await provider.GetRequiredService<IUnitOfWorkFactory>().BeginAsync();
        await orderRepo.InsertAsync(new Order(Guid.NewGuid(), "ROLLBACK-TEST"));
        await uow5.RollbackAsync();
        Console.WriteLine($"  回滚后订单数：{(await orderRepo.GetAllAsync()).Count} 条（不变）");

        Console.WriteLine();
    }

    private static async Task PrepareDatabaseAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            create schema if not exists {SchemaName};
            create table if not exists {SchemaName}.products (id uuid primary key, name text not null, stock int not null default 0);
            create table if not exists {SchemaName}.orders (id uuid primary key, order_number text not null, status int not null default 0, product_id uuid);
            delete from {SchemaName}.products;
            delete from {SchemaName}.orders;
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    // ===== 领域模型 =====

    public sealed class Product : AggregateRoot<Guid>
    {
        public Product(Guid id, string name, int stock) { Id = id; Name = name; Stock = stock; }
        private Product() { Name = string.Empty; }
        public string Name { get; private set; }
        public int Stock { get; private set; }
        public void ReduceStock(int qty) { Stock -= qty; AddDomainEvent(new StockReducedEvent(Id, qty)); }
    }

    public sealed record StockReducedEvent(Guid ProductId, int Quantity) : IEvent;

    public sealed class Order : AggregateRoot<Guid>
    {
        public Order(Guid id, string orderNumber, Guid? productId = null) { Id = id; OrderNumber = orderNumber; ProductId = productId; }
        private Order() { OrderNumber = string.Empty; }
        public string OrderNumber { get; private set; }
        public OrderStatus Status { get; private set; } = OrderStatus.Pending;
        public Guid? ProductId { get; private set; }
    }

    public enum OrderStatus { Pending, Completed }

    // ===== DbContext =====

    public sealed class SampleDbContext : DbContext
    {
        public SampleDbContext(DbContextOptions<SampleDbContext> options) : base(options) { }
        public DbSet<Product> Products => Set<Product>();
        public DbSet<Order> Orders => Set<Order>();
        protected override void OnModelCreating(ModelBuilder mb)
        {
            mb.Entity<Product>(b => { b.ToTable("products", SchemaName); b.HasKey(e => e.Id); b.Property(e => e.Id).HasColumnName("id"); b.Property(e => e.Name).HasColumnName("name"); b.Property(e => e.Stock).HasColumnName("stock"); });
            mb.Entity<Order>(b => { b.ToTable("orders", SchemaName); b.HasKey(e => e.Id); b.Property(e => e.Id).HasColumnName("id"); b.Property(e => e.OrderNumber).HasColumnName("order_number"); b.Property(e => e.Status).HasColumnName("status").HasConversion<int>(); b.Property(e => e.ProductId).HasColumnName("product_id"); });
        }
    }
}
