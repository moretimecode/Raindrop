using System.Collections;
using System.Reflection;

namespace Microsoft.Extensions.Configuration;

/// <summary>
/// 默认的配置 Key 生成器，将对象属性递归展平为
/// <c>{SectionName}:{Property}:{NestedProperty}...</c> 格式。
/// </summary>
public class DefaultConfigurationKeyGenerator : IConfigurationKeyGenerator
{
    private readonly string _sectionName;

    public DefaultConfigurationKeyGenerator(string sectionName)
    {
        _sectionName = sectionName ?? throw new ArgumentNullException(nameof(sectionName));
    }

    public Dictionary<string, string?> GenerateKeys<T>(T configObject) where T : class
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        if (configObject is not null)
        {
            GenerateKeysRecursive(configObject, _sectionName, result);
        }

        return result;
    }

    private static void GenerateKeysRecursive(object obj, string prefix, Dictionary<string, string?> result)
    {
        if (obj is null) return;

        var type = obj.GetType();

        if (IsSimpleType(type))
        {
            result[prefix] = obj.ToString();
            return;
        }

        if (obj is IEnumerable enumerable && obj is not string)
        {
            var index = 0;
            foreach (var item in enumerable)
            {
                GenerateKeysRecursive(item!, $"{prefix}:{index}", result);
                index++;
            }
            return;
        }

        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead);

        foreach (var property in properties)
        {
            var value = property.GetValue(obj);
            var key = $"{prefix}:{property.Name}";

            if (value is null)
            {
                result[key] = null;
            }
            else if (IsSimpleType(property.PropertyType))
            {
                result[key] = value.ToString();
            }
            else
            {
                GenerateKeysRecursive(value, key, result);
            }
        }
    }

    private static bool IsSimpleType(Type type)
    {
        return type.IsPrimitive ||
               type.IsEnum ||
               type == typeof(string) ||
               type == typeof(decimal) ||
               type == typeof(DateTime) ||
               type == typeof(DateTimeOffset) ||
               type == typeof(TimeSpan) ||
               type == typeof(Guid) ||
               (Nullable.GetUnderlyingType(type) is { } underlying && IsSimpleType(underlying));
    }
}
