namespace Raindrop.Data.Paging;

public class Pageable : Orderable
{
    public Pageable()
    {
    }

    public Pageable(int pageIndex, int pageSize, string? orderBy = null, bool isDesc = false)
    {
        PageIndex = pageIndex;
        PageSize = pageSize;
        OrderBy = orderBy;
        IsDesc = isDesc;
    }

    /// <summary>
    /// 当前页码，从 1 开始。
    /// </summary>
    public int PageIndex { get; init; } = 1;

    /// <summary>
    /// 每页数据条数。
    /// </summary>
    public int PageSize { get; init; } = 20;

    /// <summary>
    /// 是否刷新总数。
    /// </summary>
    public bool RefreshTotal { get; init; } = true;
}
