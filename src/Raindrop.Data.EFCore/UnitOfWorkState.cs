namespace Raindrop.Data.EFCore;

/// <summary>工作单元内部生命周期；失败与提交不明均为不可复用状态。</summary>
internal enum UnitOfWorkState
{
    Ready,
    Starting,
    Active,
    Preparing,
    Saving,
    Committing,
    Completing,
    CleaningUp,
    Failed,
    Unknown,
    Disposed
}
