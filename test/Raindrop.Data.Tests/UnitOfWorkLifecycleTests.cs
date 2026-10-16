using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Raindrop.Data.EFCore;
using Raindrop.Data.Entities;
using Raindrop.Data.Events;
using Raindrop.EventBus.Abstractions;
using Raindrop.UnitOfWork.Abstractions;

namespace Raindrop.Data.Tests;

/// <summary>
/// 只使用进程内 SQLite/InMemory，验证提交事实、事件顺序和资源所有权。
/// </summary>
public sealed class UnitOfWorkLifecycleTests
{
    [Fact]
    public async Task EventPreparation_DrainsNewBatches_AndPublishesIntegrationsAfterSave()
    {
        var trace = new List<string>();
        var bus = new RecordingBus();
        await using var fixture = await Fixture.CreateAsync(bus);
        var probe = new Probe();
        fixture.Context.Add(probe);
        probe.AddDomainEvent(new LocalStep(0));
        fixture.Context.SavingChanges += (_, _) => trace.Add("save");
        bus.Handle = (data, _) =>
        {
            switch (data)
            {
                case LocalStep step:
                    trace.Add($"local:{step.Number}");
                    if (step.Number < 2)
                        probe.AddDomainEvent(new LocalStep(step.Number + 1));
                    probe.AddDomainEvent(new RemoteFact(step.Number));
                    break;
                case RemoteFact fact:
                    trace.Add($"integration:{fact.Number}");
                    break;
            }
            return Task.CompletedTask;
        };
        fixture.UnitOfWork.OnCompleted(() => { trace.Add("completed"); return Task.CompletedTask; });

        await fixture.BeginAsync();
        await fixture.UnitOfWork.CommitAsync();

        Assert.Equal(new[] { "local:0", "local:1", "local:2", "save", "integration:0", "integration:1", "integration:2", "completed" }, trace);
        Assert.All(bus.Calls, call => Assert.Equal(call.Data.GetType(), call.GenericType));
        Assert.Empty(probe.GetDomainEvents());
    }

    [Fact]
    public async Task IntegrationOnlyBatch_IsNotLost()
    {
        var bus = new RecordingBus();
        await using var fixture = await Fixture.CreateAsync(bus);
        var probe = new Probe();
        probe.AddDomainEvent(new RemoteFact(7));
        fixture.Context.Add(probe);
        await fixture.BeginAsync();

        await fixture.UnitOfWork.CommitAsync();

        Assert.Equal(7, Assert.IsType<RemoteFact>(Assert.Single(bus.Calls).Data).Number);
        Assert.Equal(1, await fixture.Context.Probes.CountAsync());
    }

    [Fact]
    public async Task EventLoop_StopsAtBudgetBeforeSaving()
    {
        var bus = new RecordingBus();
        using var cancellation = new CancellationTokenSource();
        await using var fixture = await Fixture.CreateAsync(bus);
        var probe = new Probe();
        fixture.Context.Add(probe);
        probe.AddDomainEvent(new LocalStep(0));
        var count = 0;
        bus.Handle = (_, ct) =>
        {
            // 防止旧实现无限递归；新实现应在此兜底之前报告轮次超限。
            if (++count > 1100) cancellation.Cancel();
            ct.ThrowIfCancellationRequested();
            probe.AddDomainEvent(new LocalStep(count));
            return Task.CompletedTask;
        };
        await fixture.BeginAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.UnitOfWork.CommitAsync(cancellation.Token));

        Assert.Equal(1024, count);
        Assert.Equal(0, await fixture.Context.Probes.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task CancellationBeforePreparation_DoesNotInvokeHandlers()
    {
        var bus = new RecordingBus();
        await using var fixture = await Fixture.CreateAsync(bus);
        var probe = new Probe();
        probe.AddDomainEvent(new LocalStep(0));
        fixture.Context.Add(probe);
        await fixture.BeginAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.UnitOfWork.CommitAsync(cancellation.Token));

        Assert.Empty(bus.Calls);
        Assert.Equal(0, await fixture.Context.Probes.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task MissingBus_WithEvents_FailsBeforeSaving()
    {
        await using var fixture = await Fixture.CreateAsync();
        var probe = new Probe();
        probe.AddDomainEvent(new LocalStep(0));
        fixture.Context.Add(probe);
        await fixture.BeginAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.UnitOfWork.CommitAsync());

        Assert.Equal(0, await fixture.Context.Probes.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task MissingBus_WithoutEvents_AllowsDataOnlyCommit()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Context.Add(new Probe());
        await fixture.BeginAsync();

        await fixture.UnitOfWork.CommitAsync();

        Assert.Equal(1, await fixture.Context.Probes.CountAsync());
    }

    [Fact]
    public async Task Rollback_DiscardsCallbacksBeforeNextSuccessfulCommit()
    {
        await using var fixture = await Fixture.CreateAsync();
        var completed = false;
        fixture.UnitOfWork.OnCompleted(() => { completed = true; return Task.CompletedTask; });
        await fixture.BeginAsync();
        fixture.Context.Add(new Probe());
        await fixture.UnitOfWork.RollbackAsync();
        await fixture.BeginAsync();
        fixture.Context.Add(new Probe());

        await fixture.UnitOfWork.CommitAsync();

        Assert.False(completed);
        Assert.Equal(1, await fixture.Context.Probes.CountAsync());
    }

    [Fact]
    public async Task PreparationFailure_PreservesOriginalException_AndPreventsReuse()
    {
        var failure = new ArgumentException("领域失败");
        var bus = new RecordingBus { Handle = (_, _) => throw failure };
        await using var fixture = await Fixture.CreateAsync(bus);
        var probe = new Probe();
        probe.AddDomainEvent(new LocalStep(0));
        fixture.Context.Add(probe);
        await fixture.BeginAsync();

        Assert.Same(failure, await Record.ExceptionAsync(() => fixture.UnitOfWork.CommitAsync()));
        await fixture.UnitOfWork.RollbackAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.BeginAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.UnitOfWork.CommitAsync());
    }

    [Fact]
    public async Task SuccessfulCommits_CanReuseScope_WithoutRepeatingOldEventsOrCallbacks()
    {
        var bus = new RecordingBus();
        await using var fixture = await Fixture.CreateAsync(bus);
        var probe = new Probe();
        probe.AddDomainEvent(new RemoteFact(1));
        fixture.Context.Add(probe);
        var count = 0;
        fixture.UnitOfWork.OnCompleted(() => { count++; return Task.CompletedTask; });
        await fixture.BeginAsync();
        await fixture.UnitOfWork.CommitAsync();
        await fixture.BeginAsync();
        fixture.Context.Add(new Probe());

        await fixture.UnitOfWork.CommitAsync();

        Assert.Single(bus.Calls);
        Assert.Equal(1, count);
        Assert.Equal(2, await fixture.Context.Probes.CountAsync());
    }

    [Fact]
    public async Task DisposeUnitOfWork_DoesNotDisposeBorrowedDbContext()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.BeginAsync();

        await fixture.UnitOfWork.DisposeAsync();

        Assert.Equal(0, await fixture.Context.Probes.CountAsync());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => fixture.BeginAsync());
    }

    [Fact]
    public async Task CommitResponseFailure_PreservesUnknownAndOriginalError_WithoutNotification()
    {
        var failure = new InvalidOperationException("模拟提交成功后响应丢失");
        var interceptor = new TransactionFaults { AfterCommitFailure = failure };
        var bus = new RecordingBus();
        await using var fixture = await Fixture.CreateAsync(bus, interceptor);
        var probe = new Probe();
        probe.AddDomainEvent(new RemoteFact(1));
        fixture.Context.Add(probe);
        var completed = false;
        fixture.UnitOfWork.OnCompleted(() => { completed = true; return Task.CompletedTask; });
        await fixture.BeginAsync();

        var error = await Record.ExceptionAsync(() => fixture.UnitOfWork.CommitAsync());

        Assert.NotNull(error);
        var unknown = Assert.IsType<UnitOfWorkCommitUnknownException>(error);
        Assert.NotEqual(Guid.Empty, unknown.CommitAttemptId);
        Assert.Same(failure, unknown.InnerException);
        Assert.Equal(1, await fixture.Context.Probes.AsNoTracking().CountAsync());
        Assert.Empty(bus.Calls);
        Assert.False(completed);
        await fixture.UnitOfWork.RollbackAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.BeginAsync());
    }

    [Fact]
    public async Task SaveFailure_IsNotMaskedByRollbackOrDisposeFailure()
    {
        var failure = new ArgumentException("保存失败");
        var interceptor = new TransactionFaults { FailRollback = true };
        await using var fixture = await Fixture.CreateAsync(interceptor: interceptor, failDispose: true);
        fixture.Context.SavingChanges += (_, _) => throw failure;
        fixture.Context.Add(new Probe());
        await fixture.BeginAsync();

        Assert.Same(failure, await Record.ExceptionAsync(() => fixture.UnitOfWork.CommitAsync()));
        Assert.Equal(1, interceptor.RollbackCount);
        Assert.Equal(0, await fixture.Context.Probes.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task TransactionDisposeFailure_DoesNotChangeCommittedResultOrSkipCallbacks()
    {
        var interceptor = new TransactionFaults();
        await using var fixture = await Fixture.CreateAsync(interceptor: interceptor, failDispose: true);
        fixture.Context.Add(new Probe());
        var completed = false;
        fixture.UnitOfWork.OnCompleted(() => { completed = true; return Task.CompletedTask; });
        await fixture.BeginAsync();

        await fixture.UnitOfWork.CommitAsync();

        Assert.True(completed);
        Assert.Equal(0, interceptor.RollbackCount);
        Assert.Equal(1, await fixture.Context.Probes.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task PostCommitFailures_DoNotRepeatBusiness_AndContinueRemainingWork()
    {
        var bus = new RecordingBus
        {
            Handle = (data, _) => data is RemoteFact { Number: 1 }
                ? Task.FromException(new InvalidOperationException("发送失败"))
                : Task.CompletedTask
        };
        await using var fixture = await Fixture.CreateAsync(bus);
        var probe = new Probe();
        probe.AddDomainEvent(new RemoteFact(1));
        probe.AddDomainEvent(new RemoteFact(2));
        fixture.Context.Add(probe);
        var completed = false;
        fixture.UnitOfWork.OnCompleted(() => throw new InvalidOperationException("回调失败"));
        fixture.UnitOfWork.OnCompleted(() => { completed = true; return Task.CompletedTask; });
        await fixture.BeginAsync();

        await fixture.UnitOfWork.CommitAsync();

        Assert.True(completed);
        Assert.Equal(2, bus.Calls.Count);
        Assert.Equal(1, await fixture.Context.Probes.CountAsync());
    }

    [Fact]
    public async Task DomainHandler_CanRegisterCallbackForCurrentCommit()
    {
        var bus = new RecordingBus();
        await using var fixture = await Fixture.CreateAsync(bus);
        var probe = new Probe();
        probe.AddDomainEvent(new LocalStep(0));
        fixture.Context.Add(probe);
        var completed = false;
        bus.Handle = (_, _) =>
        {
            fixture.UnitOfWork.OnCompleted(() => { completed = true; return Task.CompletedTask; });
            return Task.CompletedTask;
        };
        await fixture.BeginAsync();

        await fixture.UnitOfWork.CommitAsync();

        Assert.True(completed);
        Assert.Equal(1, await fixture.Context.Probes.CountAsync());
    }

    [Fact]
    public async Task CommitReentry_FromHandler_IsRejectedBeforeNestedSave()
    {
        var bus = new RecordingBus();
        await using var fixture = await Fixture.CreateAsync(bus);
        var probe = new Probe();
        probe.AddDomainEvent(new LocalStep(0));
        fixture.Context.Add(probe);
        bus.Handle = async (_, _) =>
            await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.UnitOfWork.CommitAsync());
        await fixture.BeginAsync();

        await fixture.UnitOfWork.CommitAsync();

        Assert.Equal(1, await fixture.Context.Probes.CountAsync());
    }

    [Theory]
    [InlineData(EntityState.Added)]
    [InlineData(EntityState.Modified)]
    [InlineData(EntityState.Deleted)]
    public async Task Dispose_DiscardsUncommittedChanges_BeforeBorrowedContextSavesAgain(EntityState change)
    {
        await using var fixture = await Fixture.CreateAsync();
        var abandoned = new Probe { Amount = 1 };
        if (change != EntityState.Added)
        {
            fixture.Context.Add(abandoned);
            await fixture.Context.SaveChangesAsync();
        }
        await fixture.BeginAsync();
        switch (change)
        {
            case EntityState.Added: fixture.Context.Add(abandoned); break;
            case EntityState.Modified: abandoned.Amount = 2; break;
            case EntityState.Deleted: fixture.Context.Remove(abandoned); break;
        }

        await fixture.UnitOfWork.DisposeAsync();
        var later = new Probe { Amount = 3 };
        fixture.Context.Add(later);
        await fixture.Context.SaveChangesAsync();

        var saved = await fixture.Context.Probes.AsNoTracking().ToListAsync();
        Assert.Contains(saved, probe => probe.Id == later.Id && probe.Amount == 3);
        if (change == EntityState.Added)
            Assert.Single(saved);
        else
        {
            Assert.Equal(2, saved.Count);
            Assert.Contains(saved, probe => probe.Id == abandoned.Id && probe.Amount == 1);
        }
    }

    [Fact]
    public async Task Dispose_AfterCommit_PreservesCommittedRowsAndBorrowedContext()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.BeginAsync();
        fixture.Context.Add(new Probe { Amount = 1 });
        await fixture.UnitOfWork.CommitAsync();

        await fixture.UnitOfWork.DisposeAsync();
        fixture.Context.Add(new Probe { Amount = 2 });
        await fixture.Context.SaveChangesAsync();

        Assert.Equal(new[] { 1, 2 }, await fixture.Context.Probes.OrderBy(probe => probe.Amount).Select(probe => probe.Amount).ToArrayAsync());
    }

    [Fact]
    public async Task CallbackAndLoggingFailures_KeepCommittedResult_AndAllowNextUnitOfWork()
    {
        var logger = new ThrowingLogger<EntityFrameworkCoreUnitOfWork<ProbeContext>>();
        await using var fixture = await Fixture.CreateAsync(unitOfWorkLogger: logger);
        await fixture.BeginAsync();
        fixture.Context.Add(new Probe());
        var completed = false;
        fixture.UnitOfWork.OnCompleted(() => throw new InvalidOperationException("回调失败"));
        fixture.UnitOfWork.OnCompleted(() => { completed = true; return Task.CompletedTask; });

        await fixture.UnitOfWork.CommitAsync();
        await fixture.BeginAsync();
        fixture.Context.Add(new Probe());
        await fixture.UnitOfWork.CommitAsync();
        await fixture.UnitOfWork.DisposeAsync();

        Assert.True(completed);
        Assert.Equal(1, logger.Calls);
        Assert.Equal(2, await fixture.Context.Probes.CountAsync());
    }

    [Fact]
    public async Task IntegrationAndLoggingFailures_ContinueRemainingMessagesAndCallbacks()
    {
        var logger = new ThrowingLogger<DomainEventProcessor>();
        var bus = new RecordingBus
        {
            Handle = (data, _) => data is RemoteFact { Number: 1 }
                ? Task.FromException(new InvalidOperationException("发送失败"))
                : Task.CompletedTask
        };
        await using var fixture = await Fixture.CreateAsync(bus, eventLogger: logger);
        var probe = new Probe();
        probe.AddDomainEvent(new RemoteFact(1));
        probe.AddDomainEvent(new RemoteFact(2));
        fixture.Context.Add(probe);
        var completed = false;
        fixture.UnitOfWork.OnCompleted(() => { completed = true; return Task.CompletedTask; });
        await fixture.BeginAsync();

        await fixture.UnitOfWork.CommitAsync();
        await fixture.UnitOfWork.DisposeAsync();

        Assert.True(completed);
        Assert.Equal(1, logger.Calls);
        Assert.Equal(2, bus.Calls.Count);
        Assert.Equal(1, await fixture.Context.Probes.CountAsync());
    }

    [Fact]
    public async Task SaveFailure_WithRollbackDisposeAndLoggingFailures_PreservesOriginalError()
    {
        var failure = new ArgumentException("保存失败");
        var logger = new ThrowingLogger<EntityFrameworkCoreUnitOfWork<ProbeContext>>();
        var interceptor = new TransactionFaults { FailRollback = true };
        await using var fixture = await Fixture.CreateAsync(interceptor: interceptor, failDispose: true, unitOfWorkLogger: logger);
        fixture.Context.SavingChanges += (_, _) => throw failure;
        fixture.Context.Add(new Probe());
        await fixture.BeginAsync();

        Assert.Same(failure, await Record.ExceptionAsync(() => fixture.UnitOfWork.CommitAsync()));
        Assert.Equal(2, logger.Calls);
        Assert.Equal(1, interceptor.RollbackCount);
        Assert.Equal(0, await fixture.Context.Probes.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task CommitUnknown_WithRollbackLoggingFailure_PreservesIdentityAndOriginalError()
    {
        var failure = new InvalidOperationException("模拟提交响应丢失");
        var logger = new ThrowingLogger<EntityFrameworkCoreUnitOfWork<ProbeContext>>();
        var interceptor = new TransactionFaults { AfterCommitFailure = failure, FailRollback = true };
        await using var fixture = await Fixture.CreateAsync(interceptor: interceptor, unitOfWorkLogger: logger);
        fixture.Context.Add(new Probe());
        await fixture.BeginAsync();

        var unknown = await Assert.ThrowsAsync<UnitOfWorkCommitUnknownException>(() => fixture.UnitOfWork.CommitAsync());

        Assert.Same(failure, unknown.InnerException);
        Assert.NotEqual(Guid.Empty, unknown.CommitAttemptId);
        Assert.Equal(1, logger.Calls);
        Assert.Equal(1, await fixture.Context.Probes.CountAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.BeginAsync());
    }

    [Fact]
    public async Task Dispose_WithRollbackDisposeAndLoggingFailures_StillDiscardsPendingChanges()
    {
        var logger = new ThrowingLogger<EntityFrameworkCoreUnitOfWork<ProbeContext>>();
        var interceptor = new TransactionFaults { FailRollback = true };
        await using var fixture = await Fixture.CreateAsync(interceptor: interceptor, failDispose: true, unitOfWorkLogger: logger);
        await fixture.BeginAsync();
        fixture.Context.Add(new Probe());

        await fixture.UnitOfWork.DisposeAsync();
        await fixture.Context.SaveChangesAsync();

        Assert.Equal(2, logger.Calls);
        Assert.Equal(0, await fixture.Context.Probes.CountAsync());
    }

    public sealed class Probe : AggregateRoot<Guid>
    {
        public Probe() => Id = Guid.NewGuid();
        public int Amount { get; set; }
    }

    public sealed record LocalStep(int Number) : IEvent;
    public sealed record RemoteFact(int Number) : IIntegrationEvent;

    public sealed class ProbeContext(DbContextOptions<ProbeContext> options) : DbContext(options)
    {
        public DbSet<Probe> Probes => Set<Probe>();
    }

    private sealed class RecordingBus : IEventBus
    {
        public List<(IEvent Data, Type GenericType)> Calls { get; } = [];
        public Func<IEvent, CancellationToken, Task> Handle { get; set; } = (_, _) => Task.CompletedTask;

        public Task PublishAsync<TEvent>(TEvent eventData, CancellationToken cancellationToken = default) where TEvent : IEvent
        {
            Calls.Add((eventData, typeof(TEvent)));
            return Handle(eventData, cancellationToken);
        }
    }

    private sealed class Fixture(SqliteConnection connection, ServiceProvider provider) : IAsyncDisposable
    {
        public ProbeContext Context { get; } = provider.GetRequiredService<ProbeContext>();
        public IUnitOfWork UnitOfWork { get; } = provider.GetRequiredService<IUnitOfWork>();

        public static async Task<Fixture> CreateAsync(RecordingBus? bus = null, TransactionFaults? interceptor = null, bool failDispose = false, ILogger<EntityFrameworkCoreUnitOfWork<ProbeContext>>? unitOfWorkLogger = null, ILogger<DomainEventProcessor>? eventLogger = null)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            await using (var setup = new ProbeContext(new DbContextOptionsBuilder<ProbeContext>().UseSqlite(connection).Options))
                await setup.Database.EnsureCreatedAsync();
            var services = new ServiceCollection();
            services.AddRaindropData<ProbeContext>(options =>
            {
                options.UseSqlite(connection);
                if (interceptor is not null) options.AddInterceptors(interceptor);
                if (failDispose) options.ReplaceService<IRelationalTransactionFactory, ThrowingDisposeTransactionFactory>();
            });
            if (bus is not null) services.AddSingleton<IEventBus>(bus);
            if (unitOfWorkLogger is not null) services.AddSingleton(unitOfWorkLogger);
            if (eventLogger is not null) services.AddSingleton(eventLogger);
            var fixture = new Fixture(connection, services.BuildServiceProvider());
            return fixture;
        }

        public async Task BeginAsync() => await provider.GetRequiredService<IUnitOfWorkFactory>().BeginAsync();

        public async ValueTask DisposeAsync()
        {
            await provider.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class ThrowingLogger<T> : ILogger<T>
    {
        public int Calls { get; private set; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Calls++;
            throw new IOException("模拟日志提供者失败");
        }
    }

    private sealed class TransactionFaults : DbTransactionInterceptor
    {
        public Exception? AfterCommitFailure { get; init; }
        public bool FailRollback { get; init; }
        public int RollbackCount { get; private set; }

        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
            => AfterCommitFailure is null ? Task.CompletedTask : Task.FromException(AfterCommitFailure);

        public override ValueTask<InterceptionResult> TransactionRollingBackAsync(DbTransaction transaction, TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            RollbackCount++;
            if (FailRollback) throw new InvalidOperationException("回滚失败");
            return ValueTask.FromResult(result);
        }
    }

    public sealed class ThrowingDisposeTransactionFactory(RelationalTransactionFactoryDependencies dependencies) : RelationalTransactionFactory(dependencies)
    {
        public override RelationalTransaction Create(IRelationalConnection connection, DbTransaction transaction, Guid transactionId, IDiagnosticsLogger<DbLoggerCategory.Database.Transaction> logger, bool transactionOwned)
            => new ThrowingDisposeTransaction(connection, transaction, transactionId, logger, transactionOwned, Dependencies.SqlGenerationHelper);
    }

    private sealed class ThrowingDisposeTransaction(IRelationalConnection connection, DbTransaction transaction, Guid transactionId, IDiagnosticsLogger<DbLoggerCategory.Database.Transaction> logger, bool transactionOwned, ISqlGenerationHelper sqlGenerationHelper)
        : RelationalTransaction(connection, transaction, transactionId, logger, transactionOwned, sqlGenerationHelper)
    {
        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            throw new InvalidOperationException("事务释放失败");
        }
    }
}
