using Microsoft.EntityFrameworkCore;

namespace Raindrop.Data.EFCore;

/// <summary>启用 Outbox 时的 scope 内事务归属；不持有连接、不创建事务、不推测跨库原子性。</summary>
internal sealed class OutboxTransactionBinding
{
    private object? _owner;
    private DbContext? _context;
    private Action? _failRegistration;

    public void Enter(object owner, DbContext context, Action failRegistration)
    {
        if (_owner is not null && !ReferenceEquals(_owner, owner))
            throw new InvalidOperationException("启用 Outbox 后，同一 scope 不能同时开启另一个 UoW；请先完成当前事务，失败或结果不明时丢弃 scope。");
        _owner = owner;
        _context = context;
        _failRegistration = failRegistration;
    }

    public void EnsureContext(DbContext context)
    {
        if (_owner is null)
            throw new InvalidOperationException("集成事件登记要求活动的 Raindrop UoW，不能借用任意 EF 事务。");
        if (!ReferenceEquals(_context, context))
        {
            // 使真正活动的工作失败，不能误伤未使用的消息库会话后仍提交来源业务。
            _failRegistration?.Invoke();
            throw new InvalidOperationException("当前活动 UoW 的 DbContext 与配置的 Outbox DbContext 不是同一个实例，不能跨上下文登记发送意图。");
        }
    }

    public void Exit(object owner)
    {
        // DI 可能先释放另一个未使用的 UoW，只有实际所有者可以解除绑定。
        if (!ReferenceEquals(_owner, owner))
            return;
        _owner = null;
        _context = null;
        _failRegistration = null;
    }
}
