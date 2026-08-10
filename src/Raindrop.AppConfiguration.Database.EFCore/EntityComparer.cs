using System.Reflection;

namespace Microsoft.Extensions.Configuration;

/// <summary>
/// 基于反射的实体比较器，用于变更检测。
/// </summary>
public class EntityComparer<TEntity> : IEqualityComparer<TEntity> where TEntity : class
{
    public bool Equals(TEntity? x, TEntity? y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (x is null || y is null) return false;

        var properties = typeof(TEntity).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead);

        foreach (var property in properties)
        {
            var xValue = property.GetValue(x);
            var yValue = property.GetValue(y);

            if (!Equals(xValue, yValue))
                return false;
        }

        return true;
    }

    public int GetHashCode(TEntity obj)
    {
        if (obj is null) return 0;

        var hash = new HashCode();
        var properties = typeof(TEntity).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead);

        foreach (var property in properties)
        {
            hash.Add(property.GetValue(obj));
        }

        return hash.ToHashCode();
    }
}
