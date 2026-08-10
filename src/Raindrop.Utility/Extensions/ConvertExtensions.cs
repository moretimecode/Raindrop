namespace Raindrop.Utility.Extensions;

public static class ConvertExtensions
{
    /// <summary>
    /// 将对象转换为指定类型，失败时返回默认值。
    /// </summary>
    public static T? To<T>(object obj, T? defaultValue = default)
        where T : IConvertible
    {
        try
        {
            return (T)Convert.ChangeType(obj, typeof(T));
        }
        catch (FormatException)
        {
            return defaultValue;
        }
        catch (InvalidCastException)
        {
            return defaultValue;
        }
        catch (OverflowException)
        {
            return defaultValue;
        }
    }

    /// <summary>
    /// 将 IConvertible 值转换为指定类型，失败时返回默认值。
    /// </summary>
    public static T? To<T>(this IConvertible obj, T? defaultValue = default)
        where T : IConvertible
    {
        try
        {
            return (T)Convert.ChangeType(obj, typeof(T));
        }
        catch (FormatException)
        {
            return defaultValue;
        }
        catch (InvalidCastException)
        {
            return defaultValue;
        }
        catch (OverflowException)
        {
            return defaultValue;
        }
    }
}
