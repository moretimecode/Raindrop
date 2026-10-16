using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Raindrop.UnitOfWork.Abstractions;

namespace Raindrop.Data.EFCore;

/// <summary>
/// 以一条显式流程完成事件准备、保存、物理提交及后置处理。
/// 只拥有自身开启的事务，DbContext 由所属 DI scope 或调用方释放。
/// </summary>
public sealed class EntityFrameworkCoreUnitOfWork<TDbContext>(
    TDbContext context,
    DomainEventProcessor eventProcessor,
    ILogger<EntityFrameworkCoreUnitOfWork<TDbContext>>? logger = null) : IRelationalUnitOfWork
    where TDbContext : DbContext
{
    private readonly List<Func<Task>> _completedHandlers = [];
    private readonly bool _useTransactions = context.Database.IsRelational();
    private IDbContextTransaction? _dbTransaction;
    private readonly UnitOfWorkSession<TDbContext> _session = new();
    private readonly OutboxTransactionBinding? _outboxBinding;

    internal EntityFrameworkCoreUnitOfWork(TDbContext context, DomainEventProcessor eventProcessor,
        ILogger<EntityFrameworkCoreUnitOfWork<TDbContext>>? logger, UnitOfWorkSession<TDbContext> session,
        OutboxTransactionBinding? outboxBinding = null)
        : this(context, eventProcessor, logger)
    {
        _session = session;
        _outboxBinding = outboxBinding;
    }

    internal TDbContext Context => context;
    public DbConnection Connection => context.Database.GetDbConnection();
    public DbTransaction Transaction => _dbTransaction?.GetDbTransaction()
        ?? throw new InvalidOperationException("工作单元尚未开启关系型数据库事务。请先调用 IUnitOfWorkFactory.BeginAsync。");

    public async ValueTask BeginAsync(CancellationToken cancellationToken = default)
    {
        EnsureAvailable();
        if (eventProcessor.UsesOutbox != (_outboxBinding is not null))
            throw new InvalidOperationException("Outbox 工作单元必须通过 AddRaindropData 装配，并使用同一 Outbox 事件准备器；不能手工构造或覆盖依赖后丢失共享事务会话。");
        if (_session.State == UnitOfWorkState.Active)
            return;

        _outboxBinding?.Enter(_session, context, _session.FailRegistration);
        _session.State = UnitOfWorkState.Starting;
        _session.CommitAttemptId = Guid.NewGuid();
        _session.MessageIds.Clear();
        _session.TransactionId = null;
        try
        {
            if (_useTransactions)
                _dbTransaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            _session.TransactionId = _dbTransaction?.TransactionId;
            _session.State = UnitOfWorkState.Active;
        }
        catch
        {
            _session.State = UnitOfWorkState.Failed;
            _completedHandlers.Clear();
            throw;
        }
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        EnsureAvailable();
        if (_session.State != UnitOfWorkState.Active)
            throw new InvalidOperationException("工作单元提交前必须先调用 IUnitOfWorkFactory.BeginAsync。");

        _session.State = UnitOfWorkState.Preparing;
        PreparedEventDispatch prepared;
        try
        {
            prepared = await eventProcessor.PrepareAsync(context, cancellationToken).ConfigureAwait(false);
            EnsureStageSucceeded(UnitOfWorkState.Preparing);
            _session.State = UnitOfWorkState.Saving;
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            EnsureStageSucceeded(UnitOfWorkState.Saving);

            _session.State = UnitOfWorkState.Committing;
            if (_dbTransaction is not null)
                await _dbTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            _session.State = UnitOfWorkState.Completing;
        }
        catch (Exception exception)
        {
            var outcomeUnknown = _session.State == UnitOfWorkState.Committing && _dbTransaction is not null;
            _session.State = outcomeUnknown ? UnitOfWorkState.Unknown : UnitOfWorkState.Failed;
            _completedHandlers.Clear();
            await ReleaseTransactionAsync(rollback: true).ConfigureAwait(false);

            if (outcomeUnknown)
                throw new UnitOfWorkCommitUnknownException(_session.CommitAttemptId, exception, _session.MessageIds);
            throw;
        }

        // 已确认提交，释放、发送与完成回调均不能再触发业务回滚。
        try
        {
            await ReleaseTransactionAsync(rollback: false).ConfigureAwait(false);
            await prepared.CompleteAsync(cancellationToken).ConfigureAwait(false);
            await ExecuteCompletedHandlersAsync().ConfigureAwait(false);
        }
        finally
        {
            _session.State = UnitOfWorkState.Ready;
            _outboxBinding?.Exit(_session);
        }
    }

    public void OnCompleted(Func<Task> handler)
    {
        EnsureCanChangeCallbacks();
        ArgumentNullException.ThrowIfNull(handler);
        _completedHandlers.Add(handler);
    }

    public void ClearCompletedHandler()
    {
        EnsureCanChangeCallbacks();
        _completedHandlers.Clear();
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        EnsureNotBusyOrDisposed();
        var previousState = _session.State;
        _session.State = UnitOfWorkState.CleaningUp;
        _completedHandlers.Clear();
        try
        {
            // 清理不使用已取消的业务 token，避免取消阻止事务资源释放。
            await ReleaseTransactionAsync(rollback: true).ConfigureAwait(false);
            context.ChangeTracker.Clear();
        }
        finally
        {
            _session.State = previousState switch
            {
                UnitOfWorkState.Failed => UnitOfWorkState.Failed,
                UnitOfWorkState.Unknown => UnitOfWorkState.Unknown,
                _ => UnitOfWorkState.Ready
            };
            if (_session.State == UnitOfWorkState.Ready)
                _outboxBinding?.Exit(_session);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_session.State == UnitOfWorkState.Disposed)
            return;
        EnsureNotBusyOrDisposed();
        _session.State = UnitOfWorkState.Disposed;
        _completedHandlers.Clear();
        try
        {
            await ReleaseTransactionAsync(rollback: true).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                // DbContext 仍由外部持有，必须防止后续保存重放已放弃的跟踪变更。
                context.ChangeTracker.Clear();
            }
            finally
            {
                _outboxBinding?.Exit(_session);
            }
        }
    }

    private async Task ExecuteCompletedHandlersAsync()
    {
        var handlers = _completedHandlers.ToArray();
        _completedHandlers.Clear();
        foreach (var handler in handlers)
        {
            try
            {
                await handler().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                UnitOfWorkDiagnostics.TryLog(() => logger?.LogError(exception, "OnCompleted 回调执行失败"));
            }
        }
    }

    private async Task ReleaseTransactionAsync(bool rollback)
    {
        var transaction = _dbTransaction;
        _dbTransaction = null;
        if (transaction is null)
            return;

        if (rollback)
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                UnitOfWorkDiagnostics.TryLog(() => logger?.LogError(exception, "工作单元回滚失败，提交尝试: {CommitAttemptId}", _session.CommitAttemptId));
            }
        }

        try
        {
            await transaction.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            UnitOfWorkDiagnostics.TryLog(() => logger?.LogError(exception, "工作单元事务释放失败，提交尝试: {CommitAttemptId}", _session.CommitAttemptId));
        }
    }

    private void EnsureStageSucceeded(UnitOfWorkState expected)
    {
        if (_session.State != expected)
            throw new InvalidOperationException("当前工作单元的事件登记已经失败，不能提交业务变更。");
    }

    private void EnsureCanChangeCallbacks()
    {
        ObjectDisposedException.ThrowIf(_session.State == UnitOfWorkState.Disposed, this);
        if (_session.State is not (UnitOfWorkState.Ready or UnitOfWorkState.Active or UnitOfWorkState.Preparing))
            throw new InvalidOperationException("当前阶段不能修改工作单元完成回调。");
    }

    private void EnsureAvailable()
    {
        EnsureNotBusyOrDisposed();
        if (_session.State is UnitOfWorkState.Failed or UnitOfWorkState.Unknown)
            throw new InvalidOperationException("当前工作单元已经失败或提交结果不明，必须丢弃当前 scope，不能继续使用。");
    }

    private void EnsureNotBusyOrDisposed()
    {
        ObjectDisposedException.ThrowIf(_session.State == UnitOfWorkState.Disposed, this);
        if (_session.IsRegistering || _session.State is UnitOfWorkState.Starting or UnitOfWorkState.Preparing or UnitOfWorkState.Committing
            or UnitOfWorkState.Saving or UnitOfWorkState.Completing or UnitOfWorkState.CleaningUp)
            throw new InvalidOperationException("工作单元正在执行操作，不支持并发或重入。");
    }
}
