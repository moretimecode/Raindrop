using System.Linq.Expressions;
using Raindrop.Data.Entities;
using Raindrop.Data.Specifications;
using Raindrop.UnitOfWork.Abstractions;

namespace Raindrop.Data.Repository;

/// <summary>
/// 聚合根仓储接口。
/// 负责聚合根的持久化，查询通过简单的 Expression 谓词或原生 SQL 完成。
/// 复杂查询（需要 Include 导航属性）使用 ISpecification。
/// </summary>
public interface IRepository<TEntity, TPrimaryKey>
    where TEntity : AggregateRoot<TPrimaryKey>
{
    // ── 写 ──

    /// <summary>插入聚合根。</summary>
    Task InsertAsync(TEntity entity);

    /// <summary>更新聚合根。</summary>
    Task UpdateAsync(TEntity entity);

    /// <summary>删除聚合根。</summary>
    Task DeleteAsync(TEntity entity);

    /// <summary>插入聚合根并返回主键值（适用于自增 ID）。</summary>
    Task<TPrimaryKey> InsertAndGetIdAsync(TEntity entity);

    /// <summary>执行原生 SQL 命令（非查询）。适用于批量更新/删除等操作。</summary>
    Task ExecuteCmd(string sql, params object[] parameters);

    // ── 查 ──

    /// <summary>查询所有聚合根。</summary>
    Task<List<TEntity>> GetAllAsync();

    /// <summary>按主键查询聚合根。不存在时返回 null。</summary>
    Task<TEntity?> GetAsync(TPrimaryKey id);

    /// <summary>按谓词查询列表。TQuery 必须是 DbContext 中注册的实体类型。</summary>
    Task<List<TQuery>> QueryAsync<TQuery>(Expression<Func<TQuery, bool>> predicate)
        where TQuery : class;

    /// <summary>按谓词查单条。TQuery 必须是 DbContext 中注册的实体类型。不存在时返回 null。</summary>
    Task<TQuery?> FirstOrDefaultAsync<TQuery>(Expression<Func<TQuery, bool>> predicate)
        where TQuery : class;

    /// <summary>按规约查询（支持 Include 导航属性、排序）。</summary>
    Task<List<TEntity>> GetListAsync(ISpecification<TEntity> specification);

    // ── 工作单元 ──

    /// <summary>当前仓储关联的工作单元。多个仓储在同一个 scope 内共享同一个 UoW。</summary>
    IUnitOfWork UnitOfWork { get; }
}
