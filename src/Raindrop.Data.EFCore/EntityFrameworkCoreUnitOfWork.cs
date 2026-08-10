using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Raindrop.EventBus.Abstractions;
using Raindrop.UnitOfWork.Abstractions;

namespace Raindrop.Data.EFCore;

/// <summary>
/// EF Core 工作单元实现。
/// 管理显式数据库事务，确保 ChangeTracker 中的全部修改在一个事务内提交或回滚。
///
/// CommitAsync 流程：
///   收集领域事件 → 递归处理 → SaveChanges → 提交事务 → 发布集成事件 → 执行完成回调
///
/// 事务通过 IUnitOfWorkFactory.BeginAsync 显式开启，提交后自动释放。
/// InMemory 等不支持事务的 provider 会跳过事务管理。
///
/// OnCompleted 回调与主事务切割：回调在主事务提交之后执行，单个回调失败只记录错误日志，
/// 不会导致 CommitAsync 抛异常，也不会回滚已提交的数据。
/// </summary>
public sealed class EntityFrameworkCoreUnitOfWork<TDbContext> : IRelationalUnitOfWork
    where TDbContext : DbContext
{
    private readonly TDbContext _context;
    private readonly DomainEventProcessor? _domainEventProcessor;
    private readonly List<Func<Task>> _completedHandlers = [];
    private readonly bool _useTransactions;
    private readonly ILogger<EntityFrameworkCoreUnitOfWork<TDbContext>>? _logger;
    private IDbContextTransaction? _dbTransaction;
    private bool _began;
    private bool _disposed;

    public EntityFrameworkCoreUnitOfWork(
        TDbContext context,
        IEventBus? eventBus = null,
        ILogger<DomainEventProcessor>? domainEventLogger = null,
        ILogger<EntityFrameworkCoreUnitOfWork<TDbContext>>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        _domainEventProcessor = eventBus is not null
            ? new DomainEventProcessor(eventBus, domainEventLogger)
            : null;
        _useTransactions = CanUseTransactions(context);
        _logger = logger;
    }

    internal TDbContext Context => _context;

    public DbConnection Connection => _context.Database.GetDbConnection();

    public DbTransaction Transaction => _dbTransaction?.GetDbTransaction()
        ?? throw new InvalidOperationException("工作单元尚未开启关系型数据库事务。请先调用 IUnitOfWorkFactory.BeginAsync。");

    /// <summary>
    /// 显式开启工作单元。
    /// 关系型 provider 会在这里开启数据库事务；InMemory 等 provider 跳过事务管理。
    /// </summary>
    public async ValueTask BeginAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_began)
        {
            return;
        }

        _began = true;

        if (!_useTransactions)
        {
            return;
        }

        try
        {
            _dbTransaction = await _context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            _began = false;
            throw;
        }
    }

    public void OnCompleted(Func<Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _completedHandlers.Add(handler);
    }

    public void ClearCompletedHandler()
    {
        _completedHandlers.Clear();
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_began)
        {
            throw new InvalidOperationException("工作单元提交前必须先调用 IUnitOfWorkFactory.BeginAsync。");
        }

        try
        {
            if (_useTransactions)
            {
                await CommitWithTransactionAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await CommitWithoutTransactionAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _began = false;
        }
    }

    private async Task CommitWithTransactionAsync(CancellationToken cancellationToken)
    {
        var dbTransaction = _dbTransaction
            ?? throw new InvalidOperationException("关系型工作单元提交前必须先调用 IUnitOfWorkFactory.BeginAsync。");

        try
        {
            List<IIntegrationEvent> integrationEvents = [];

            if (_domainEventProcessor is not null)
            {
                integrationEvents = await _domainEventProcessor.ProcessAsync(_context, cancellationToken);
            }

            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await dbTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            if (_domainEventProcessor is not null && integrationEvents.Count > 0)
            {
                await _domainEventProcessor.PublishIntegrationEventsAsync(integrationEvents, cancellationToken);
            }

            await ExecuteCompletedHandlersAsync();
        }
        catch
        {
            await TryRollbackTransactionAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            await TryDisposeTransactionAsync().ConfigureAwait(false);
        }
    }

    private async Task CommitWithoutTransactionAsync(CancellationToken cancellationToken)
    {
        // InMemory 等 provider 不支持事务，跳过事务管理
        List<IIntegrationEvent> integrationEvents = [];

        if (_domainEventProcessor is not null)
        {
            integrationEvents = await _domainEventProcessor.ProcessAsync(_context, cancellationToken);
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (_domainEventProcessor is not null && integrationEvents.Count > 0)
        {
            await _domainEventProcessor.PublishIntegrationEventsAsync(integrationEvents, cancellationToken);
        }

        await ExecuteCompletedHandlersAsync();
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_useTransactions)
        {
            await TryRollbackTransactionAsync().ConfigureAwait(false);
        }

        _began = false;
        _context.ChangeTracker.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            await TryRollbackTransactionAsync().ConfigureAwait(false);
            await _context.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 执行所有已注册的 OnCompleted 回调。
    /// 每个回调独立 try-catch：单个失败记日志后继续执行下一个，不会中断其他回调，也不会向上抛异常。
    /// </summary>
    private async Task ExecuteCompletedHandlersAsync()
    {
        foreach (var handler in _completedHandlers)
        {
            try
            {
                await handler();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "OnCompleted 回调执行失败");
            }
        }

        _completedHandlers.Clear();
    }

    private async Task TryRollbackTransactionAsync()
    {
        if (_dbTransaction is not null)
        {
            try
            {
                await _dbTransaction.RollbackAsync().ConfigureAwait(false);
            }
            catch
            {
                // Rollback 失败不影响上层
            }
            finally
            {
                await _dbTransaction.DisposeAsync().ConfigureAwait(false);
                _dbTransaction = null;
            }
        }
    }

    private async Task TryDisposeTransactionAsync()
    {
        if (_dbTransaction is not null)
        {
            await _dbTransaction.DisposeAsync().ConfigureAwait(false);
            _dbTransaction = null;
        }
    }

    private static bool CanUseTransactions(TDbContext context)
    {
        var providerName = context.Database.ProviderName;
        return providerName is not null
            && !providerName.Contains("InMemory", StringComparison.OrdinalIgnoreCase);
    }
}
