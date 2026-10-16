using System.Data.Common;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Raindrop.Data.EFCore;
using Raindrop.Data.Entities;
using Raindrop.Data.Events;
using Raindrop.EventBus.Abstractions;
using Raindrop.EventBus.Core;
using Raindrop.UnitOfWork.Abstractions;

namespace Raindrop.Data.Tests;

public sealed class OutboxRegistrationTests
{
    [Fact]
    public async Task BothPublishOrigins_CommitWithBusiness_WithoutSending()
    {
        await using var f = await Fixture.CreateAsync();
        await f.BeginAsync();
        var entity = new BusinessRow { Value = "业务变更" };
        entity.AddDomainEvent(new Fact { Text = "聚合事件" });
        f.Context.Add(entity);
        await f.Bus.PublishAsync(new Fact { Text = "直接发布" });
        Assert.Empty(await f.Context.Set<OutboxMessage>().AsNoTracking().ToListAsync());

        await f.Uow.CommitAsync();

        var messages = await f.MessagesAsync();
        Assert.Equal(2, messages.Count);
        Assert.Equal(2, messages.Select(x => x.MessageId).Distinct().Count());
        Assert.Single(messages.Select(x => x.CommitAttemptId).Distinct());
        Assert.All(messages, x => { Assert.Equal("Pending", x.Status); Assert.Equal("fact.created", x.ContractName); Assert.Equal("1", x.ContractVersion); });
        Assert.Equal(new[] { "直接发布", "聚合事件" }.Order(), messages.Select(ReadText).Order());
        Assert.Equal(1, await f.Context.Rows.CountAsync());
        Assert.Equal(0, f.Transport.SendCount);
    }

    [Fact]
    public async Task SavedBusinessAndOutbox_RollBackTogether()
    {
        await using var f = await Fixture.CreateAsync();
        await f.BeginAsync();
        f.Context.Add(new BusinessRow());
        await f.Bus.PublishAsync(new Fact());
        await f.Context.SaveChangesAsync();
        Assert.Single(await f.MessagesAsync());

        await f.Uow.RollbackAsync();

        Assert.Empty(await f.MessagesAsync());
        Assert.Empty(await f.Context.Rows.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task BusinessConstraintFailure_RollsBackOutbox()
    {
        await using var f = await Fixture.CreateAsync();
        await f.BeginAsync();
        f.Context.Add(new BusinessRow { Value = null });
        await f.Bus.PublishAsync(new Fact());

        await Assert.ThrowsAsync<DbUpdateException>(() => f.Uow.CommitAsync());

        Assert.Empty(await f.MessagesAsync());
        Assert.Empty(await f.Context.Rows.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Publish_FreezesRuntimePayloadTargetAndHash()
    {
        await using var f = await Fixture.CreateAsync();
        await f.BeginAsync();
        IIntegrationEvent fact = new Fact { Text = "原内容" };
        await f.Bus.PublishAsync(fact);
        ((Fact)fact).Text = "后续修改";
        f.Resolver.Key = "changed";
        await f.Uow.CommitAsync();

        var message = Assert.Single(await f.MessagesAsync());
        Assert.Equal("原内容", ReadText(message));
        Assert.Contains("original", message.TargetJson);
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(message.Payload)), message.PayloadHash);
        var copy = message.Payload;
        copy[0] = 0;
        Assert.Equal("原内容", ReadText(message));
        Assert.Equal(typeof(Fact).AssemblyQualifiedName, message.LegacyTypeName);
    }

    [Theory]
    [InlineData("no-route")]
    [InlineData("local-fallback")]
    [InlineData("mixed")]
    [InlineData("duplicate-target")]
    [InlineData("duplicate-adapter")]
    [InlineData("missing-adapter")]
    [InlineData("unsupported")]
    [InlineData("second-target-fails")]
    [InlineData("missing-contract")]
    [InlineData("serialization")]
    [InlineData("cancelled")]
    public async Task InvalidRegistration_IsAtomicAndPoisonsActiveWork(string scenario)
    {
        await using var f = await Fixture.CreateAsync(scenario);
        await f.BeginAsync();
        f.Context.Add(new BusinessRow());
        using var cts = new CancellationTokenSource();
        if (scenario == "cancelled") cts.Cancel();
        IIntegrationEvent fact = scenario == "missing-contract" ? new UnregisteredFact() :
            scenario == "serialization" ? new UnserializableFact() : new Fact();

        await Assert.ThrowsAnyAsync<Exception>(() => f.Bus.PublishAsync(fact, cts.Token));

        Assert.Empty(f.Context.ChangeTracker.Entries<OutboxMessage>());
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Uow.CommitAsync());
        Assert.Equal(0, f.Transport.SendCount);
        Assert.Equal(0, f.Transport.LocalCount);
        await f.Uow.RollbackAsync();
        Assert.Empty(await f.MessagesAsync());
    }

    [Fact]
    public async Task OutsideRaindropUow_IsRejected_WithoutPoisoningFutureWork()
    {
        await using var f = await Fixture.CreateAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Bus.PublishAsync(new Fact()));
        await using (var rawTransaction = await f.Context.Database.BeginTransactionAsync())
            await Assert.ThrowsAsync<InvalidOperationException>(() => f.Bus.PublishAsync(new Fact()));
        await f.BeginAsync();
        await f.Bus.PublishAsync(new Fact());
        await f.Uow.CommitAsync();
        Assert.Single(await f.MessagesAsync());
    }

    [Fact]
    public async Task MultipleTargets_ShareMessageIdentityAndBytes()
    {
        await using var f = await Fixture.CreateAsync("two-targets");
        await f.BeginAsync();
        await f.Bus.PublishAsync(new Fact());
        await f.Uow.CommitAsync();
        var messages = await f.MessagesAsync();
        Assert.Equal(2, messages.Count);
        Assert.Single(messages.Select(x => x.MessageId).Distinct());
        Assert.Equal(2, messages.Select(x => x.DeliveryId).Distinct().Count());
        Assert.Equal(messages[0].Payload, messages[1].Payload);
        Assert.NotEqual(messages[0].TargetJson, messages[1].TargetJson);
    }

    [Fact]
    public async Task RecursiveDomainHandler_RegistersIntegrationBeforeSave()
    {
        await using var f = await Fixture.CreateAsync();
        var row = new BusinessRow();
        row.AddDomainEvent(new LocalFact());
        f.Context.Add(row);
        f.Transport.LocalHandler = () => row.AddDomainEvent(new Fact { Text = "派生事件" });
        await f.BeginAsync();
        await f.Uow.CommitAsync();
        Assert.Equal("派生事件", ReadText(Assert.Single(await f.MessagesAsync())));
        Assert.Equal(1, f.Transport.LocalCount);
        Assert.Equal(0, f.Transport.SendCount);
    }

    [Fact]
    public async Task SwallowedRegistrationFailureInDomainHandler_CannotCommitBusiness()
    {
        await using var f = await Fixture.CreateAsync();
        var row = new BusinessRow();
        row.AddDomainEvent(new LocalFact());
        f.Context.Add(row);
        f.Transport.LocalHandlerAsync = async () =>
        {
            try { await f.Bus.PublishAsync(new UnregisteredFact()); }
            catch (InvalidOperationException) { }
        };
        await f.BeginAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Uow.CommitAsync());
        Assert.Empty(await f.Context.Rows.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task PlainDomainEvent_WithRemoteRoute_IsRejectedBeforeSending()
    {
        await using var f = await Fixture.CreateAsync("remote-domain");
        var row = new BusinessRow();
        row.AddDomainEvent(new LocalFact());
        f.Context.Add(row);
        await f.BeginAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Uow.CommitAsync());
        Assert.Equal(0, f.Transport.SendCount);
        Assert.Empty(await f.Context.Rows.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task NextCommit_UsesNewAttemptAndDoesNotRepeatOldMessage()
    {
        await using var f = await Fixture.CreateAsync();
        for (var i = 0; i < 2; i++)
        {
            await f.BeginAsync();
            await f.Bus.PublishAsync(new Fact());
            await f.Uow.CommitAsync();
        }
        var rows = await f.MessagesAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(2, rows.Select(x => x.CommitAttemptId).Distinct().Count());
    }

    [Fact]
    public async Task CommitUnknown_PreservesOriginalIdsOfDurableMessages()
    {
        await using var f = await Fixture.CreateAsync(afterCommitFailure: true);
        await f.BeginAsync();
        f.Context.Add(new BusinessRow());
        await f.Bus.PublishAsync(new Fact());
        var exception = await Assert.ThrowsAsync<UnitOfWorkCommitUnknownException>(() => f.Uow.CommitAsync());
        var message = Assert.Single(await f.MessagesAsync());
        Assert.Equal(message.CommitAttemptId, exception.CommitAttemptId);
        Assert.Equal(new[] { message.MessageId }, exception.MessageIds);
        Assert.Equal(1, await f.Context.Rows.AsNoTracking().CountAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Bus.PublishAsync(new Fact()));
        Assert.Equal(0, f.Transport.SendCount);
    }

    [Fact]
    public async Task SavedChangesCallback_CannotRegisterTooLateOrHideFailure()
    {
        await using var f = await Fixture.CreateAsync();
        f.Context.Add(new BusinessRow());
        f.Context.SavedChanges += (_, _) =>
        {
            try { f.Bus.PublishAsync(new Fact()).GetAwaiter().GetResult(); }
            catch (InvalidOperationException) { }
        };
        await f.BeginAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Uow.CommitAsync());
        Assert.Empty(await f.Context.Rows.AsNoTracking().ToListAsync());
        Assert.Empty(await f.MessagesAsync());
    }

    [Fact]
    public async Task Serialization_CannotReenterCommitAndEscapeCurrentRegistration()
    {
        await using var f = await Fixture.CreateAsync();
        f.Context.Add(new BusinessRow());
        await f.BeginAsync();
        await Assert.ThrowsAnyAsync<Exception>(() => f.Bus.PublishAsync(new ReentrantFact(() => f.Uow.CommitAsync())));
        Assert.Empty(await f.Context.Rows.AsNoTracking().ToListAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Uow.CommitAsync());
    }

    [Fact]
    public async Task MissingModel_RejectsRegistrationAndCommit()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddRaindropData<UnmappedContext>(o => o.UseSqlite(connection));
        services.AddRaindropOutbox<UnmappedContext>(o => o.AddContract<Fact>("fact", "1"));
        await using var provider = services.BuildServiceProvider();
        var uow = await provider.GetRequiredService<IUnitOfWorkFactory>().BeginAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetRequiredService<IEventOutbox>().EnqueueAsync(new Fact()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => uow.CommitAsync());
    }

    [Fact]
    public async Task NonRelationalProvider_CannotPretendToProvideAtomicOutbox()
    {
        var services = new ServiceCollection();
        services.AddRaindropData<TestContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddRaindropOutbox<TestContext>(o => o.AddContract<Fact>("fact", "1"));
        await using var provider = services.BuildServiceProvider();
        var uow = await provider.GetRequiredService<IUnitOfWorkFactory>().BeginAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetRequiredService<IEventOutbox>().EnqueueAsync(new Fact()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => uow.CommitAsync());
    }

    [Fact]
    public async Task RealLocalBusRegistration_DoesNotRequireIntegrationHandler()
    {
        await using var f = await Fixture.CreateAsync("real-local");
        await f.BeginAsync();
        await f.Bus.PublishAsync(new Fact());
        await f.Uow.CommitAsync();
        Assert.Single(await f.MessagesAsync());
        Assert.Equal(0, f.Transport.LocalCount);
        Assert.Equal(0, f.Transport.SendCount);
    }

    [Fact]
    public async Task ActualDefaultLocalFallback_CannotAcceptIntegrationEvent()
    {
        await using var f = await Fixture.CreateAsync("real-local-fallback");
        await f.BeginAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Bus.PublishAsync(new PlainFact()));
        Assert.Equal(0, f.Transport.LocalCount);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Uow.CommitAsync());
    }

    [Fact]
    public void ContractRegistration_RejectsCollisionsAndDuplicateOutbox()
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentException>(() => services.AddRaindropOutbox<TestContext>(o =>
        {
            o.AddContract<Fact>("fact", "1");
            o.AddContract<UnregisteredFact>("fact", "1");
        }));
        services.AddRaindropOutbox<TestContext>(o => o.AddContract<Fact>("fact", "1"));
        Assert.Throws<InvalidOperationException>(() => services.AddRaindropOutbox<TestContext>(o => o.AddContract<Fact>("fact", "1")));
    }

    internal static string ReadText(OutboxMessage message)
    {
        using var json = JsonDocument.Parse(message.Payload);
        return json.RootElement.GetProperty("Text").GetString() ?? "";
    }

    [Remote("original")]
    public sealed class Fact : IIntegrationEvent { public string Text { get; set; } = "事件"; }
    public sealed class ReentrantFact(Func<Task> commit) : IIntegrationEvent
    {
        public string Text { get { commit().GetAwaiter().GetResult(); return "重入"; } }
    }
    public sealed record PlainFact : IIntegrationEvent;
    public sealed class UnmappedContext(DbContextOptions<UnmappedContext> options) : DbContext(options);
    public sealed record UnregisteredFact : IIntegrationEvent;
    public sealed class UnserializableFact : IIntegrationEvent { public Action Callback { get; } = () => { }; }
    public sealed record LocalFact : IEvent;
    public sealed class BusinessRow : AggregateRoot<Guid>
    {
        public BusinessRow() { Id = Guid.NewGuid(); }
        public string? Value { get; set; } = "业务";
    }
    public sealed class TestContext(DbContextOptions<TestContext> options) : DbContext(options)
    {
        public DbSet<BusinessRow> Rows => Set<BusinessRow>();
        protected override void OnModelCreating(ModelBuilder builder)
        {
            if (Database.IsNpgsql()) builder.HasDefaultSchema("raindrop_outbox_atomic_tests");
            builder.Entity<BusinessRow>().Property(x => x.Value).IsRequired();
            builder.AddRaindropOutbox();
        }
    }
    public sealed class RemoteAttribute(string key) : EventRouteAttribute { public string Key { get; } = key; }
    internal sealed class Resolver(string scenario) : IEventRouteResolver
    {
        public string Key { get; set; } = "original";
        public IReadOnlyCollection<EventRoute> Resolve(Type eventType)
        {
            if (eventType == typeof(LocalFact) && scenario != "remote-domain") return [new(new LocalEventAttribute())];
            return scenario switch
            {
                "no-route" or "real-local-fallback" => [],
                "local-fallback" => [new(new LocalEventAttribute())],
                "mixed" => [new(new LocalEventAttribute()), new(new RemoteAttribute(Key))],
                "duplicate-target" => [new(new RemoteAttribute(Key)), new(new RemoteAttribute(Key))],
                "two-targets" or "second-target-fails" => [new(new RemoteAttribute(Key)), new(new RemoteAttribute("second"))],
                _ => [new(new RemoteAttribute(Key))]
            };
        }
    }
    internal sealed class Transport(string scenario) : IEventTransport
    {
        public int SendCount { get; private set; }
        public int LocalCount { get; private set; }
        public Action? LocalHandler { get; set; }
        public Func<Task>? LocalHandlerAsync { get; set; }
        public bool CanHandle(EventRoute route) => scenario != "missing-adapter";
        public EventDeliveryTarget CaptureTarget(EventRoute route)
        {
            if (scenario == "unsupported") throw new NotSupportedException("不支持持久化目标");
            var key = ((RemoteAttribute)route.Metadata).Key;
            if (scenario == "second-target-fails" && key == "second") throw new InvalidOperationException("目标无效");
            return new("test", "1", JsonSerializer.Serialize(new { key }));
        }
        public async Task PublishAsync<TEvent>(TEvent data, EventRoute route, CancellationToken ct = default) where TEvent : IEvent
        {
            if (route.Metadata is LocalEventAttribute)
            {
                LocalCount++;
                LocalHandler?.Invoke();
                if (LocalHandlerAsync is not null) await LocalHandlerAsync();
            }
            else SendCount++;
        }
    }
    internal sealed class Fixture(ServiceProvider provider, SqliteConnection? connection) : IAsyncDisposable
    {
        public TestContext Context => provider.GetRequiredService<TestContext>();
        public IUnitOfWork Uow => provider.GetRequiredService<IUnitOfWork>();
        public async Task BeginAsync() => await provider.GetRequiredService<IUnitOfWorkFactory>().BeginAsync();
        public IEventBus Bus => provider.GetRequiredService<IEventBus>();
        public Transport Transport => provider.GetRequiredService<Transport>();
        public Resolver Resolver => provider.GetRequiredService<Resolver>();
        public Task<List<OutboxMessage>> MessagesAsync() => Context.Set<OutboxMessage>().AsNoTracking().ToListAsync();
        public static async Task<Fixture> CreateAsync(string scenario = "", bool afterCommitFailure = false, string? postgres = null)
        {
            SqliteConnection? connection = null;
            if (postgres is null) { connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync(); }
            var fault = new CommitFault();
            var services = new ServiceCollection();
            services.AddRaindropData<TestContext>(o =>
            {
                if (connection is not null) o.UseSqlite(connection);
                else o.UseNpgsql(postgres ?? throw new InvalidOperationException("测试数据库未配置"));
                if (afterCommitFailure) o.AddInterceptors(fault);
            });
            services.AddSingleton(new Resolver(scenario));
            services.AddSingleton<IEventRouteResolver>(p => p.GetRequiredService<Resolver>());
            services.AddSingleton(new Transport(scenario));
            services.AddSingleton<IEventTransport>(p => p.GetRequiredService<Transport>());
            if (scenario == "duplicate-adapter") services.AddSingleton<IEventTransport>(new Transport(scenario));
            services.AddScoped<IEventBus, RoutingEventBus>();
            if (scenario.StartsWith("real-local", StringComparison.Ordinal))
                services.AddMediatRLocalEventBus(typeof(IEventBus).Assembly);
            services.AddRaindropOutbox<TestContext>(o =>
            {
                o.AddContract<Fact>("fact.created", "1");
                o.AddContract<ReentrantFact>("reentrant.created", "1");
                o.AddContract<PlainFact>("plain.created", "1");
                o.AddContract<UnserializableFact>("bad.created", "1");
            });
            var f = new Fixture(services.BuildServiceProvider(), connection);
            if (postgres is null) await f.Context.Database.EnsureCreatedAsync();
            else
            {
                await f.Context.Database.ExecuteSqlRawAsync("DROP SCHEMA IF EXISTS raindrop_outbox_atomic_tests CASCADE");
                await f.Context.Database.ExecuteSqlRawAsync(f.Context.Database.GenerateCreateScript());
            }
            fault.Enabled = true;
            return f;
        }
        public async ValueTask DisposeAsync()
        {
            if (connection is null) await Context.Database.ExecuteSqlRawAsync("DROP SCHEMA IF EXISTS raindrop_outbox_atomic_tests CASCADE");
            await provider.DisposeAsync();
            if (connection is not null) await connection.DisposeAsync();
        }
    }
    private sealed class CommitFault : DbTransactionInterceptor
    {
        public bool Enabled { get; set; }
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
            => Enabled ? Task.FromException(new IOException("提交已完成但确认丢失")) : Task.CompletedTask;
    }
}
