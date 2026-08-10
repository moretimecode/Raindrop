namespace Raindrop.Data.Paging;

public class PageableResultSummary<TItem, TSummary> : PageableResult<TItem>
{
    public PageableResultSummary()
    {
    }

    public PageableResultSummary(long totalCount, List<TItem> items)
        : base(totalCount, items)
    {
    }

    public PageableResultSummary(long totalCount, List<TItem> items, TSummary summary)
        : base(totalCount, items)
    {
        Summary = summary;
    }

    /// <summary>
    /// 当前分页结果对应的汇总数据。
    /// </summary>
    public TSummary? Summary { get; set; }
}
