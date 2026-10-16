namespace Raindrop.UnitOfWork.Abstractions;

/// <summary>
/// 工作单元接口。负责事务边界管理、领域事件递归处理、集成事件发布和完成回调。
/// </summary>
public interface IUnitOfWork : IAsyncDisposable
{
    /// <summary>
    /// 提交所有变更。
    /// EF 实现先准备领域事件；启用 Outbox 时在原事务登记集成事件，再 SaveChanges 和物理提交。
    /// 默认未启用 Outbox 时，确认提交后尽力发布集成事件；启用时仅持久化意图，不即时发送。
    /// 确认提交后执行 OnCompleted。
    /// 物理提交结果不明时抛出 UnitOfWorkCommitUnknownException，失败实例不可重用。
    /// </summary>
    Task CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 回滚并清理跟踪变更与完成回调。不能将提交不明解释为确定未提交。
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
