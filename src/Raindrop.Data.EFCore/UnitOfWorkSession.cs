namespace Raindrop.Data.EFCore;

/// <summary>同一 DbContext 的事务会话，供 UoW 与登记器共享，不持有发送能力。</summary>
internal sealed class UnitOfWorkSession<TDbContext> where TDbContext : Microsoft.EntityFrameworkCore.DbContext
{
    public UnitOfWorkState State { get; set; } = UnitOfWorkState.Ready;
    public Guid CommitAttemptId { get; set; }
    public Guid? TransactionId { get; set; }
    public List<string> MessageIds { get; } = [];
    public bool IsRegistering { get; set; }

    public void FailRegistration()
    {
        if (State is UnitOfWorkState.Active or UnitOfWorkState.Preparing or UnitOfWorkState.Saving)
            State = UnitOfWorkState.Failed;
    }

    public void EnsureRegistrationAllowed()
    {
        if (State == UnitOfWorkState.Saving)
        {
            State = UnitOfWorkState.Failed;
            throw new InvalidOperationException("工作单元已开始最终保存，不能在保存拦截器或回调中追加发送意图。");
        }
        if (State is not (UnitOfWorkState.Active or UnitOfWorkState.Preparing))
            throw new InvalidOperationException("集成事件登记要求活动的 Raindrop 工作单元；失败或提交结果不明时必须丢弃当前 scope。");
    }
}
