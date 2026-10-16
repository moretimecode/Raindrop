using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Raindrop.UnitOfWork.Abstractions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Raindrop.Data.EFCore;
using Raindrop.Data.Entities;
using Raindrop.Data.Events;
using Raindrop.EventBus.Abstractions;
using Raindrop.EventBus.Core;

namespace Raindrop.Data.Tests;

/// <summary>使用两个独立物理数据库，验证发送登记不能借用另一个 UoW 的事务。</summary>
public sealed class OutboxTransactionOwnershipTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OverlappingContexts_CannotBeginIndependentTransactions(bool firstOwnsOutbox)
    {
        await using var f = await Fixture.CreateAsync();
        if (firstOwnsOutbox)
        {
            await f.OutboxUow.BeginAsync();
            // 原 review 复现先开启 A 再开启 B；修复必须在开启第二个事务时拒绝。
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await f.OtherUow.BeginAsync());
            Assert.Null(f.OtherContext.Database.CurrentTransaction);
            await f.Bus.PublishAsync(new Fact("原所有者仍正常"));
            await f.OutboxUow.CommitAsync();
            Assert.Single(await f.OutboxContext.Set<OutboxMessage>().AsNoTracking().ToListAsync());
        }
        else
        {
            await f.OtherUow.BeginAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await f.OutboxUow.BeginAsync());
            Assert.Null(f.OutboxContext.Database.CurrentTransaction);
            f.OtherContext.Add(new BusinessRow());
            await f.OtherUow.CommitAsync();
            Assert.Equal(1, await f.OtherContext.Rows.CountAsync());
        }
    }

    [Fact]
    public async Task OtherContextAggregateEvent_CannotBeStoredInConfiguredOutbox()
    {
        await using var f = await Fixture.CreateAsync();
        await f.OtherUow.BeginAsync();
        var row = new BusinessRow();
        row.AddDomainEvent(new Fact("另一数据库的聚合事件"));
        f.OtherContext.Add(row);

        await Assert.ThrowsAsync<InvalidOperationException>(() => f.OtherUow.CommitAsync());

        Assert.Empty(await f.OtherContext.Rows.AsNoTracking().ToListAsync());
        Assert.Empty(f.OutboxContext.ChangeTracker.Entries<OutboxMessage>());
        Assert.Empty(await f.OutboxContext.Set<OutboxMessage>().AsNoTracking().ToListAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await f.OutboxUow.BeginAsync());
    }

    [Fact]
    public async Task SwallowedDirectPublishFailure_PoisonsActualOwnerInsteadOfUnusedOutboxSession()
    {
        await using var f = await Fixture.CreateAsync();
        await f.OtherUow.BeginAsync();
        f.OtherContext.Add(new BusinessRow());

        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Bus.PublishAsync(new Fact("错误归属")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.OtherUow.CommitAsync());
        await f.OtherUow.RollbackAsync();

        Assert.Empty(await f.OtherContext.Rows.AsNoTracking().ToListAsync());
        Assert.Empty(f.OutboxContext.ChangeTracker.Entries<OutboxMessage>());
        // 失败终态不能因 Rollback 而允许另一 UoW 在相同 scope 接管。
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await f.OutboxUow.BeginAsync());
    }

    [Fact]
    public async Task SwallowedPublishFailureInsideOtherDomainHandler_CannotCommitBusiness()
    {
        await using var f = await Fixture.CreateAsync();
        await f.OtherUow.BeginAsync();
        var row = new BusinessRow();
        row.AddDomainEvent(new LocalFact());
        f.OtherContext.Add(row);
        f.Transport.LocalHandler = async () =>
        {
            try { await f.Bus.PublishAsync(new Fact("本地 handler 中发布")); }
            catch (InvalidOperationException) { }
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => f.OtherUow.CommitAsync());

        Assert.Empty(await f.OtherContext.Rows.AsNoTracking().ToListAsync());
        Assert.Empty(f.OutboxContext.ChangeTracker.Entries<OutboxMessage>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task IndependentContexts_CanRunSequentiallyAfterCompletionOrRollback(bool commitFirst)
    {
        await using var f = await Fixture.CreateAsync();
        await f.OtherUow.BeginAsync();
        f.OtherContext.Add(new BusinessRow());
        if (commitFirst) await f.OtherUow.CommitAsync();
        else await f.OtherUow.RollbackAsync();

        await f.OutboxUow.BeginAsync();
        // 同一个 UoW 重复 Begin 不创建新所有者或新事务。
        var transactionId = f.OutboxContext.Database.CurrentTransaction?.TransactionId;
        await f.OutboxUow.BeginAsync();
        Assert.Equal(transactionId, f.OutboxContext.Database.CurrentTransaction?.TransactionId);
        var row = new BusinessRow();
        row.AddDomainEvent(new Fact("合法聚合事件"));
        f.OutboxContext.Add(row);
        await f.Bus.PublishAsync(new Fact("合法直接发布"));
        await f.OutboxUow.CommitAsync();

        Assert.Equal(commitFirst ? 1 : 0, await f.OtherContext.Rows.AsNoTracking().CountAsync());
        Assert.Equal(1, await f.OutboxContext.Rows.AsNoTracking().CountAsync());
        Assert.Equal(2, await f.OutboxContext.Set<OutboxMessage>().AsNoTracking().CountAsync());
        Assert.Equal(0, f.Transport.SendCount);
        await f.OtherUow.BeginAsync();
        f.OtherContext.Add(new BusinessRow());
        await f.OtherUow.CommitAsync();
        Assert.Equal(commitFirst ? 2 : 1, await f.OtherContext.Rows.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task DisposeUnusedContext_DoesNotReleaseActiveOwnerBinding()
    {
        await using var f = await Fixture.CreateAsync();
        await f.OutboxUow.BeginAsync();
        await f.OtherUow.DisposeAsync();
        await f.Bus.PublishAsync(new Fact("释放非所有者后仍有效"));
        await f.OutboxUow.CommitAsync();
        Assert.Single(await f.OutboxContext.Set<OutboxMessage>().AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task OutboxRollback_ReleasesBindingForOtherNonMessagingTransaction()
    {
        await using var f = await Fixture.CreateAsync();
        await f.OutboxUow.BeginAsync();
        await f.Bus.PublishAsync(new Fact("放弃发送"));
        await f.OutboxContext.SaveChangesAsync();
        await f.OutboxUow.RollbackAsync();
        await f.OtherUow.BeginAsync();
        f.OtherContext.Add(new BusinessRow());
        await f.OtherUow.CommitAsync();
        Assert.Equal(1, await f.OtherContext.Rows.CountAsync());
        Assert.Empty(await f.OutboxContext.Set<OutboxMessage>().AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task ManualOutboxUow_CannotBypassSharedScopeOwnership()
    {
        await using var f = await Fixture.CreateAsync();
        await f.OutboxUow.BeginAsync();
        await using var manual = new EntityFrameworkCoreUnitOfWork<OtherContext>(f.OtherContext, f.EventProcessor);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await manual.BeginAsync());
        Assert.Null(f.OtherContext.Database.CurrentTransaction);
        await f.Bus.PublishAsync(new Fact("正确的事务继续工作"));
        await f.OutboxUow.CommitAsync();
        Assert.Single(await f.OutboxContext.Set<OutboxMessage>().AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task LegacyEventProcessorOverride_CannotDisableEnabledOutboxSilently()
    {
        await using var f = await Fixture.CreateAsync(useLegacyProcessor: true);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await f.OutboxUow.BeginAsync());
        Assert.Null(f.OutboxContext.Database.CurrentTransaction);
    }

    [Fact]
    public async Task CommitUnknown_RollbackCannotAllowAnotherContextToTakeOver()
    {
        await using var f = await Fixture.CreateAsync(afterCommitFailure: true);
        await f.OutboxUow.BeginAsync();
        f.OutboxContext.Add(new BusinessRow());
        await f.Bus.PublishAsync(new Fact("保留原事务身份"));
        var error = await Assert.ThrowsAsync<UnitOfWorkCommitUnknownException>(() => f.OutboxUow.CommitAsync());
        await f.OutboxUow.RollbackAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await f.OtherUow.BeginAsync());
        var persisted = Assert.Single(await f.OutboxContext.Set<OutboxMessage>().AsNoTracking().ToListAsync());
        Assert.Equal(persisted.CommitAttemptId, error.CommitAttemptId);
        Assert.Equal(new[] { persisted.MessageId }, error.MessageIds);
    }

    public sealed record Fact(string Text) : IIntegrationEvent;
    public sealed record LocalFact : IEvent;
    public sealed class BusinessRow : AggregateRoot<Guid>
    {
        public BusinessRow() { Id = Guid.NewGuid(); }
    }
    public sealed class OutboxContext(DbContextOptions<OutboxContext> options) : DbContext(options)
    {
        public DbSet<BusinessRow> Rows => Set<BusinessRow>();
        protected override void OnModelCreating(ModelBuilder builder) => builder.AddRaindropOutbox();
    }
    public sealed class OtherContext(DbContextOptions<OtherContext> options) : DbContext(options)
    {
        public DbSet<BusinessRow> Rows => Set<BusinessRow>();
    }
    private sealed class RemoteMetadata : EventRouteAttribute;
    private sealed class Resolver : IEventRouteResolver
    {
        public IReadOnlyCollection<EventRoute> Resolve(Type type) => type == typeof(LocalFact)
            ? [new(new LocalEventAttribute())] : [new(new RemoteMetadata())];
    }
    private sealed class Transport : IEventTransport
    {
        public int SendCount { get; private set; }
        public Func<Task>? LocalHandler { get; set; }
        public bool CanHandle(EventRoute route) => true;
        public EventDeliveryTarget CaptureTarget(EventRoute route) => new("test", "1", "{}");
        public async Task PublishAsync<T>(T data, EventRoute route, CancellationToken cancellationToken = default) where T : IEvent
        {
            if (route.Metadata is LocalEventAttribute)
            {
                if (LocalHandler is not null) await LocalHandler();
            }
            else SendCount++;
        }
    }
    private sealed class CommitFault : DbTransactionInterceptor
    {
        public bool Enabled { get; set; }
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
            => Enabled ? Task.FromException(new IOException("物理提交已完成，确认异常")) : Task.CompletedTask;
    }
    private sealed class Fixture(ServiceProvider provider, AsyncServiceScope scope, SqliteConnection outboxConnection,
        SqliteConnection otherConnection) : IAsyncDisposable
    {
        public OutboxContext OutboxContext => scope.ServiceProvider.GetRequiredService<OutboxContext>();
        public OtherContext OtherContext => scope.ServiceProvider.GetRequiredService<OtherContext>();
        public EntityFrameworkCoreUnitOfWork<OutboxContext> OutboxUow => scope.ServiceProvider.GetRequiredService<EntityFrameworkCoreUnitOfWork<OutboxContext>>();
        public EntityFrameworkCoreUnitOfWork<OtherContext> OtherUow => scope.ServiceProvider.GetRequiredService<EntityFrameworkCoreUnitOfWork<OtherContext>>();
        public DomainEventProcessor EventProcessor => scope.ServiceProvider.GetRequiredService<DomainEventProcessor>();
        public IEventBus Bus => scope.ServiceProvider.GetRequiredService<IEventBus>();
        public Transport Transport => scope.ServiceProvider.GetRequiredService<Transport>();
        public static async Task<Fixture> CreateAsync(bool afterCommitFailure = false, bool useLegacyProcessor = false)
        {
            var outboxConnection = new SqliteConnection("Data Source=:memory:");
            var otherConnection = new SqliteConnection("Data Source=:memory:");
            await outboxConnection.OpenAsync();
            await otherConnection.OpenAsync();
            var fault = new CommitFault();
            var services = new ServiceCollection();
            if (useLegacyProcessor)
                services.AddScoped(sp => new DomainEventProcessor(sp.GetRequiredService<IEventBus>()));
            services.AddRaindropData<OutboxContext>(o =>
            {
                o.UseSqlite(outboxConnection);
                if (afterCommitFailure) o.AddInterceptors(fault);
            });
            services.AddRaindropData<OtherContext>(o => o.UseSqlite(otherConnection));
            services.AddScoped<IEventBus, RoutingEventBus>();
            services.AddSingleton<IEventRouteResolver, Resolver>();
            services.AddSingleton<Transport>();
            services.AddSingleton<IEventTransport>(sp => sp.GetRequiredService<Transport>());
            services.AddRaindropOutbox<OutboxContext>(o => o.AddContract<Fact>("ownership.fact", "1"));
            var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            var scope = provider.CreateAsyncScope();
            var fixture = new Fixture(provider, scope, outboxConnection, otherConnection);
            await fixture.OutboxContext.Database.EnsureCreatedAsync();
            await fixture.OtherContext.Database.EnsureCreatedAsync();
            fault.Enabled = true;
            return fixture;
        }
        public async ValueTask DisposeAsync()
        {
            await scope.DisposeAsync();
            await provider.DisposeAsync();
            await outboxConnection.DisposeAsync();
            await otherConnection.DisposeAsync();
        }
    }
}
