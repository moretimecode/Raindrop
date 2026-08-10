namespace Raindrop.UnitOfWork.Abstractions;

/// <summary>
/// 工作单元接口。负责事务边界管理、领域事件递归处理、集成事件发布和完成回调。
/// </summary>
public interface IUnitOfWork : IAsyncDisposable
{
    /// <summary>
    /// 提交所有变更。
    /// 内部流程：收集领域事件 → 递归处理领域事件 → SaveChanges → 发布集成事件 → 执行 OnCompleted 回调。
    /// </summary>
    Task CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 回滚。丢弃所有未持久化的变更。
    /// </summary>
    Task RollbackAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 注册一个提交成功后执行的回调。
    /// CommitAsync 成功时依次执行所有已注册的 handler。
    /// CommitAsync 失败时不执行。
    /// </summary>
    void OnCompleted(Func<Task> handler);

    /// <summary>
    /// 清理所有已注册的完成回调。
    /// </summary>
    void ClearCompletedHandler();
}
