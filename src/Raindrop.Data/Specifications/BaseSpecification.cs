using System.Linq.Expressions;

namespace Raindrop.Data.Specifications;

/// <summary>
/// 规约模式基类。继承该类并调用基类方法构建查询规约。
/// </summary>
public abstract class BaseSpecification<TEntity> : ISpecification<TEntity>
{
    protected BaseSpecification(Expression<Func<TEntity, bool>>? criteria = null)
    {
        Criteria = criteria;
    }

    public Expression<Func<TEntity, bool>>? Criteria { get; }

    public IReadOnlyList<Expression<Func<TEntity, object>>> Includes => _includes.AsReadOnly();
    private readonly List<Expression<Func<TEntity, object>>> _includes = [];

    public IReadOnlyList<string> IncludeStrings => _includeStrings.AsReadOnly();
    private readonly List<string> _includeStrings = [];

    public Expression<Func<TEntity, object>>? OrderBy { get; private set; }

    public Expression<Func<TEntity, object>>? OrderByDescending { get; private set; }

    public int Take { get; private set; }

    public int Skip { get; private set; }

    public bool IsPagingEnabled { get; private set; }

    /// <summary>添加 Include 导航属性。</summary>
    protected void AddInclude(Expression<Func<TEntity, object>> includeExpression)
    {
        _includes.Add(includeExpression);
    }

    /// <summary>添加字符串形式的 Include（支持 ThenInclude 路径）。</summary>
    protected void AddInclude(string includeString)
    {
        _includeStrings.Add(includeString);
    }

    /// <summary>启用分页。</summary>
    protected void ApplyPaging(int skip, int take)
    {
        Skip = skip;
        Take = take;
        IsPagingEnabled = true;
    }

    /// <summary>设置正序排序。</summary>
    protected void ApplyOrderBy(Expression<Func<TEntity, object>> orderByExpression)
    {
        OrderBy = orderByExpression;
    }

    /// <summary>设置倒序排序。</summary>
    protected void ApplyOrderByDescending(Expression<Func<TEntity, object>> orderByDescendingExpression)
    {
        OrderByDescending = orderByDescendingExpression;
    }
}
