namespace Raindrop.UnitOfWork.Abstractions;

/// <summary>
/// 物理提交调用发生异常，无法确认事务是否已经提交。必须保留原尝试身份并核实，不能盲目重做业务。
/// 此异常本身不提供持久提交凭据或自动核实能力。
/// </summary>
public sealed class UnitOfWorkCommitUnknownException(Guid commitAttemptId, Exception innerException, IEnumerable<string>? messageIds = null)
    : InvalidOperationException($"工作单元提交结果不明，提交尝试标识为 {commitAttemptId}。请核实原事务结果，不要直接重试业务。", innerException)
{
    /// <summary>原物理提交尝试的身份，不因资源清理或回滚尝试改变。</summary>
    public Guid CommitAttemptId { get; } = commitAttemptId;

    /// <summary>原事务内已分配的逻辑消息身份，只能用于核实，不能视为已提交证明。</summary>
    public IReadOnlyList<string> MessageIds { get; } = Array.AsReadOnly(messageIds?.ToArray() ?? []);
}
