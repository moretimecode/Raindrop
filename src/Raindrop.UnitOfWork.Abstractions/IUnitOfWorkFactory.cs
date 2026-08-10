namespace Raindrop.UnitOfWork.Abstractions;

/// <summary>
/// 工作单元工厂。
/// 作为手动开启工作单元的入口，BeginAsync 返回前必须完成工作单元初始化。
/// </summary>
public interface IUnitOfWorkFactory
{
    /// <summary>
    /// 开启一个工作单元。
    /// 关系型实现应在这里打开连接并开启数据库事务。
    /// </summary>
    ValueTask<IUnitOfWork> BeginAsync(CancellationToken cancellationToken = default);
}
