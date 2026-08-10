using System.Linq.Expressions;

namespace Raindrop.Data.Specifications;

/// <summary>
/// 规约模式接口，用于封装查询条件、排序、 Include 和分页。
/// </summary>
public interface ISpecification<TEntity>
{
    /// <summary>查询条件（WHERE 子句）。</summary>
    Expression<Func<TEntity, bool>>? Criteria { get; }

    /// <summary>需要 Include 的导航属性。</summary>
    IReadOnlyList<Expression<Func<TEntity, object>>> Includes { get; }

    /// <summary>字符串形式的 Include（如 "Orders.Items.Product"）。</summary>
    IReadOnlyList<string> IncludeStrings { get; }

    /// <summary>排序字段（正序）。</summary>
    Expression<Func<TEntity, object>>? OrderBy { get; }

    /// <summary>排序字段（倒序）。</summary>
    Expression<Func<TEntity, object>>? OrderByDescending { get; }

    /// <summary>分页 — 每页条数。</summary>
    int Take { get; }

    /// <summary>分页 — 跳过条数。</summary>
    int Skip { get; }

    /// <summary>是否启用分页。</summary>
    bool IsPagingEnabled { get; }
}
