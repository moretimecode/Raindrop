namespace Raindrop.Utility.Extensions;

public static class DateTimeExtensions

{
    /// <summary>
    /// DateTime 转 Unix 时间戳（秒）。
    /// </summary>
    public static long ToTimestamp(this DateTime value)
    {
        return new DateTimeOffset(value).ToUnixTimeSeconds();
    }

    /// <summary>
    /// Unix 时间戳（秒）转 DateTime（本地时间）。
    /// </summary>
    public static DateTime ToDateTime(this long value)
    {
        return DateTimeOffset.FromUnixTimeSeconds(value).LocalDateTime;
    }

    /// <summary>
    /// Unix 时间戳转可空 DateTime。0 视为无值，返回 null。
    /// </summary>
    public static DateTime? ToNullableDateTime(this long value)
    {
        return value == 0 ? null : ToDateTime(value);
    }

    /// <summary>
    /// 判断指定日期是否在当前月份内。
    /// </summary>
    public static bool IsCurrentMonth(this DateTime dateTime)
    {
        var today = DateTime.Today;
        var monthStart = today.BeginOfMonth();
        var monthEnd = today.EndOfMonth();

        return dateTime >= monthStart && dateTime <= monthEnd;
    }

    /// <summary>
    /// 获取指定日期的开始时间（00:00:00.000）。
    /// </summary>
    public static DateTime BeginOfDay(this DateTime dateTime)
    {
        return dateTime.Date;
    }

    /// <summary>
    /// 获取指定日期的最后时刻（23:59:59.9999999）。
    /// </summary>
    public static DateTime EndOfDay(this DateTime value)
    {
        return value.Date.AddDays(1).AddTicks(-1);
    }

    /// <summary>
    /// 获取指定日期当月第一天（00:00:00.000）。
    /// </summary>
    public static DateTime BeginOfMonth(this DateTime date)
    {
        return new DateTime(date.Year, date.Month, 1);
    }

    /// <summary>
    /// 获取指定日期当月最后一天最后一刻。
    /// </summary>
    public static DateTime EndOfMonth(this DateTime date)
    {
        return date.BeginOfMonth().AddMonths(1).AddTicks(-1);
    }
}
