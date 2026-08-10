using System.Collections;
using System.Globalization;
using System.Reflection;
using Raindrop.Data.Events;
using Raindrop.EventBus.Abstractions;

namespace Raindrop.EventBus.Core;

public sealed class PropertyPathConsumerIdempotencyKeyResolver : IConsumerIdempotencyKeyResolver
{
    public string Resolve<TEvent>(
        TEvent eventData,
        Type handlerType,
        IdempotentConsumerAttribute attribute)
        where TEvent : IEvent
    {
        ArgumentNullException.ThrowIfNull(eventData);
        ArgumentNullException.ThrowIfNull(handlerType);
        ArgumentNullException.ThrowIfNull(attribute);

        if (attribute.KeyProperties.Length == 0)
        {
            throw new InvalidOperationException($"{handlerType.FullName} 的幂等配置至少需要一个 Key 属性。");
        }

        var prefix = string.IsNullOrWhiteSpace(attribute.KeyPrefix)
            ? handlerType.FullName ?? handlerType.Name
            : attribute.KeyPrefix;
        var values = attribute.KeyProperties
            .Select(path => ResolvePropertyPath(eventData, path))
            .Select(value => Convert.ToString(value, CultureInfo.InvariantCulture))
            .Select(value => string.IsNullOrWhiteSpace(value)
                ? throw new InvalidOperationException($"{handlerType.FullName} 的幂等 Key 属性值不能为空。")
                : value)
            .ToArray();

        return string.Join(attribute.Separator, values.Prepend(prefix));
    }

    private static object ResolvePropertyPath(object source, string propertyPath)
    {
        if (string.IsNullOrWhiteSpace(propertyPath))
        {
            throw new InvalidOperationException("幂等 Key 属性路径不能为空。");
        }

        object? current = source;

        foreach (var segment in propertyPath.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (current is null)
            {
                throw new InvalidOperationException($"幂等 Key 属性路径 {propertyPath} 的中间值为空。");
            }

            current = ResolveSegment(current, segment, propertyPath);
        }

        return current ?? throw new InvalidOperationException($"幂等 Key 属性路径 {propertyPath} 的值为空。");
    }

    private static object? ResolveSegment(object source, string segment, string propertyPath)
    {
        var bracketIndex = segment.IndexOf('[', StringComparison.Ordinal);
        var propertyName = bracketIndex < 0 ? segment : segment[..bracketIndex];
        var value = GetPropertyValue(source, propertyName, propertyPath);

        if (bracketIndex < 0)
        {
            return value;
        }

        var endBracketIndex = segment.IndexOf(']', bracketIndex);
        if (endBracketIndex < 0)
        {
            throw new InvalidOperationException($"幂等 Key 属性路径 {propertyPath} 的数组索引格式不正确。");
        }

        var indexText = segment.Substring(bracketIndex + 1, endBracketIndex - bracketIndex - 1);
        var index = int.Parse(indexText, CultureInfo.InvariantCulture);

        return value switch
        {
            IList list => list[index],
            IEnumerable enumerable => enumerable.Cast<object?>().ElementAt(index),
            _ => throw new InvalidOperationException($"幂等 Key 属性路径 {propertyPath} 的 {propertyName} 不是集合。")
        };
    }

    private static object? GetPropertyValue(object source, string propertyName, string propertyPath)
    {
        var property = source.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);

        if (property is null)
        {
            throw new InvalidOperationException($"没有找到幂等 Key 属性路径 {propertyPath} 中的 {propertyName}。");
        }

        return property.GetValue(source);
    }
}
