namespace Raindrop.Data.Paging;

public class PageableResult<T>
{
    public PageableResult()
    {
        Items = [];
    }

    public PageableResult(long totalCount, List<T> items)
        : this(totalCount, items, refreshedTotal: true)
    {
    }

    public PageableResult(long totalCount, List<T> items, bool refreshedTotal)
    {
        TotalCount = totalCount;
        Items = items.AsReadOnly();
        RefreshedTotal = refreshedTotal;
    }

    /// <summary>
    /// 数据列表。
    /// </summary>
    public IReadOnlyList<T> Items { get; init; }

    /// <summary>
    /// 总记录数。
    /// </summary>
    public long TotalCount { get; init; }

    /// <summary>
    /// 是否已经刷新总数。
    /// </summary>
    public bool RefreshedTotal { get; init; } = true;
}
