namespace Raindrop.Utility;

/// <summary>
/// 表示一段连续的时间范围。
/// </summary>
public class DateTimePeriod
{
    public DateTime StartTime { get; }
    public DateTime EndTime { get; }

    public DateTimePeriod(DateTime startTime, DateTime endTime)
    {
        if (startTime > endTime)
            throw new ArgumentException("结束时间不能早于开始时间。", nameof(endTime));

        StartTime = startTime;
        EndTime = endTime;
    }

    /// <summary>
    /// 判断指定时间是否在区间内。
    /// </summary>
    public bool Contains(DateTime dateTime, IntervalKind kind = IntervalKind.LeftClosedRightOpen)
    {
        return kind switch
        {
            IntervalKind.Closed => dateTime >= StartTime && dateTime <= EndTime,
            IntervalKind.Open => dateTime > StartTime && dateTime < EndTime,
            IntervalKind.LeftClosedRightOpen => dateTime >= StartTime && dateTime < EndTime,
            IntervalKind.LeftOpenRightClosed => dateTime > StartTime && dateTime <= EndTime,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }
}

/// <summary>
/// 时间区间的开闭类型。
/// </summary>
public enum IntervalKind
{
    /// <summary>[start, end] 闭区间</summary>
    Closed,

    /// <summary>(start, end) 开区间</summary>
    Open,

    /// <summary>[start, end) 左闭右开</summary>
    LeftClosedRightOpen,

    /// <summary>(start, end] 左开右闭</summary>
    LeftOpenRightClosed
}
