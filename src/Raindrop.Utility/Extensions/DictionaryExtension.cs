namespace Raindrop.Utility.Extensions;

public static class DictionaryExtension
{
    /// <summary>
    /// 安全获取字典值，Key 不存在或转换失败时返回默认值。
    /// </summary>
    public static T? TryGet<T>(this IDictionary<string, object> dic, string key, T? defaultValue = default)
        where T : IConvertible
    {
        if (dic == null || dic.Count == 0 || !dic.TryGetValue(key, out var value))
            return defaultValue;

        return ConvertExtensions.To<T>(value);
    }

    /// <summary>
    /// 添加或替换字典中的值。
    /// </summary>
    public static void AddOrReplace<TKey, TValue>(this IDictionary<TKey, TValue> dictionary, TKey key, TValue value)
    {
        dictionary[key] = value;
    }
}
