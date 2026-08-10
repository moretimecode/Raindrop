using System.Data.Common;

namespace Raindrop.UnitOfWork.Abstractions;

/// <summary>
/// 关系型数据库工作单元。
/// 用于 EF Core、ADO.NET、Dapper 等共享同一个连接和事务。
/// </summary>
public interface IRelationalUnitOfWork : IUnitOfWork
{
    /// <summary>
    /// 当前工作单元使用的数据库连接。
    /// </summary>
    DbConnection Connection { get; }

    /// <summary>
    /// 当前工作单元已经开启的数据库事务。
    /// </summary>
    DbTransaction Transaction { get; }
}
