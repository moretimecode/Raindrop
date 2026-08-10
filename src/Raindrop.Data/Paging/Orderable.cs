namespace Raindrop.Data.Paging;

public abstract class Orderable
{
    /// <summary>
    /// 排序字段。为 null 时由调用方或查询实现决定默认排序。
    /// </summary>
    public string? OrderBy { get; set; }

    /// <summary>
    /// 是否倒序排序。
    /// </summary>
    public bool IsDesc { get; set; }
}
