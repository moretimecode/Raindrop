using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Raindrop.Data.EFCore;
using Raindrop.Data.Entities;
using Raindrop.Data.Events;
using Raindrop.Data.Repository;
using Raindrop.EventBus.Abstractions;
using Raindrop.EventBus.Core;
using Raindrop.EventBus.MediatR;
using Raindrop.UnitOfWork.Abstractions;

namespace Raindrop.Data.Tests;

/// <summary>
/// 验证领域事件 handler 异常时 CommitAsync 必须失败，数据不落库。
/// ThrowingHandler 放在独立文件中避免污染 MediatR 程序集扫描。
/// </summary>
public class DomainEventExceptionTests
{
    // ===== 领域模型 =====

    public sealed class Order : AggregateRoot<Guid>
    {
        public Order(Guid id, string orderNumber)
        {
            Id = id;
            OrderNumber = orderNumber;
        }

        private Order() { OrderNumber = string.Empty; }

        public string OrderNumber { get; private set; }
        public OrderStatus Status { get; private set; } = OrderStatus.Pending;

        public void Complete()
        {
            Status = OrderStatus.Completed;
            AddDomainEvent(new TestOrderSubmittedEvent(Id));
        }
    }

    public enum OrderStatus { Pending, Completed }

    public sealed record TestOrderSubmittedEvent(Guid OrderId) : IEvent;

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
    }

    // ===== DbContext =====

    public sealed class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }

        public DbSet<Order> Orders => Set<Order>();
        public DbSet<Product> Products => Set<Product>();

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
        }
    }

    // ===== ThrowingHandler（在独立类中，不被 MediatR 程序集扫描到） =====

    public sealed class ThrowingOrderSubmittedHandler : IEventHandler<TestOrderSubmittedEvent>
    {
        public Task HandleAsync(TestOrderSubmittedEvent eventData, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Simulated domain event handler exception");
        }
    }

    // ===== 正常的无异常 handler（用于对比验证） =====

    public sealed class NormalOrderSubmittedHandler : IEventHandler<TestOrderSubmittedEvent>
    {
        public Task HandleAsync(TestOrderSubmittedEvent eventData, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    // ===== Helper =====

    private static ServiceProvider BuildProvider(string dbName, bool useThrowingHandler)
    {
        var services = new ServiceCollection();
        services.AddRaindropData<TestDbContext>(o => o.UseInMemoryDatabase(dbName));

        // 注册 handler（手动，不经过程序集扫描）
        if (useThrowingHandler)
        {
            services.AddScoped<IEventHandler<TestOrderSubmittedEvent>, ThrowingOrderSubmittedHandler>();
        }
        else
        {
            services.AddScoped<IEventHandler<TestOrderSubmittedEvent>, NormalOrderSubmittedHandler>();
        }

        // 用 AddMediatRLocalEventBus 但传入空程序集数组
        // handler 已经在上面手动注册了
        var assembly = typeof(DomainEventExceptionTests).Assembly;
        services.AddMediatR(cfg =>
        {
            cfg.Lifetime = ServiceLifetime.Scoped;
            cfg.RegisterServicesFromAssembly(assembly);
        });
        services.AddScoped<IEventTransport, MediatRLocalEventTransport>();
        services.AddEventBusCore<MediatREventRouteResolver>();

        // 扫描 IEventHandler 并注册 MediatR 适配器
        var registrations = EventHandlerRegistrationScanner.Scan([assembly]);
        foreach (var reg in registrations)
        {
            var notificationHandlerType = typeof(INotificationHandler<>).MakeGenericType(
                typeof(MediatREventNotification<>).MakeGenericType(reg.EventType));
            var adapterType = typeof(MediatREventNotificationHandler<>).MakeGenericType(reg.EventType);
            services.TryAddEnumerable(ServiceDescriptor.Transient(notificationHandlerType, adapterType));
        }

        return services.BuildServiceProvider();
    }

    private static async Task CommitAsync(IServiceProvider provider)
    {
        var uow = await provider.GetRequiredService<IUnitOfWorkFactory>().BeginAsync();
        await uow.CommitAsync();
    }

    // ===== Tests =====

    [Fact]
    public async Task ThrowingDomainEventHandler_PreventsSave()
    {
        var dbName = nameof(ThrowingDomainEventHandler_PreventsSave);
        var provider = BuildProvider(dbName, useThrowingHandler: true);
        var orderRepo = provider.GetRequiredService<IRepository<Order, Guid>>();
        var productRepo = provider.GetRequiredService<IRepository<Product, Guid>>();

        var pId = Guid.NewGuid();
        var oId = Guid.NewGuid();

        await productRepo.InsertAsync(new Product(pId, "Item", 10));
        await orderRepo.InsertAsync(new Order(oId, "ORD-001"));
        await CommitAsync(provider);

        var order = await orderRepo.GetAsync(oId);
        order.Complete();

        // ThrowingOrderSubmittedHandler 抛异常 → CommitAsync 必须失败
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CommitAsync(provider));

        // 新 provider 验证数据没有持久化
        var verifyProvider = BuildProvider(dbName, useThrowingHandler: false);
        var verifyOrderRepo = verifyProvider.GetRequiredService<IRepository<Order, Guid>>();
        var savedOrder = await verifyOrderRepo.GetAsync(oId);
        Assert.NotNull(savedOrder);
        Assert.Equal(OrderStatus.Pending, savedOrder.Status);

        // Stock 没有被扣减（handler 异常了，SaveChanges 没执行）
        var verifyProductRepo = verifyProvider.GetRequiredService<IRepository<Product, Guid>>();
        var savedProduct = await verifyProductRepo.GetAsync(pId);
        Assert.NotNull(savedProduct);
        Assert.Equal(10, savedProduct.Stock);
    }

    [Fact]
    public async Task NormalDomainEventHandler_DoesNotThrow()
    {
        var dbName = nameof(NormalDomainEventHandler_DoesNotThrow);
        var provider = BuildProvider(dbName, useThrowingHandler: false);
        var orderRepo = provider.GetRequiredService<IRepository<Order, Guid>>();

        await orderRepo.InsertAsync(new Order(Guid.NewGuid(), "ORD-NORMAL"));
        await CommitAsync(provider);

        var order = await orderRepo.GetAsync(Guid.NewGuid());
        Assert.Null(order);
    }
}
