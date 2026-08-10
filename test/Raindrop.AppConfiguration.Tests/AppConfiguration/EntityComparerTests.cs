using Microsoft.Extensions.Configuration;

namespace Raindrop.AppConfiguration.Tests;

/// <summary>
/// EntityComparer 的单元测试。
/// 验证基于反射的实体相等性判断和哈希码生成。
/// </summary>
public class EntityComparerTests
{
    private sealed record Person(string Name, int Age);

    private readonly EntityComparer<Person> _comparer = new();

    [Fact]
    public void Equals_SameReference_ReturnsTrue()
    {
        var person = new Person("Alice", 30);

        Assert.True(_comparer.Equals(person, person));
    }

    [Fact]
    public void Equals_SameValues_ReturnsTrue()
    {
        var x = new Person("Bob", 25);
        var y = new Person("Bob", 25);

        Assert.True(_comparer.Equals(x, y));
    }

    [Fact]
    public void Equals_DifferentName_ReturnsFalse()
    {
        var x = new Person("Charlie", 40);
        var y = new Person("David", 40);

        Assert.False(_comparer.Equals(x, y));
    }

    [Fact]
    public void Equals_DifferentAge_ReturnsFalse()
    {
        var x = new Person("Eve", 28);
        var y = new Person("Eve", 29);

        Assert.False(_comparer.Equals(x, y));
    }

    [Fact]
    public void Equals_BothNull_ReturnsTrue()
    {
        Assert.True(_comparer.Equals(null, null));
    }

    [Fact]
    public void Equals_FirstNull_ReturnsFalse()
    {
        Assert.False(_comparer.Equals(null, new Person("Frank", 50)));
    }

    [Fact]
    public void Equals_SecondNull_ReturnsFalse()
    {
        Assert.False(_comparer.Equals(new Person("Grace", 60), null));
    }

    [Fact]
    public void GetHashCode_SameValues_ReturnsSameHash()
    {
        var x = new Person("Heidi", 33);
        var y = new Person("Heidi", 33);

        Assert.Equal(_comparer.GetHashCode(x), _comparer.GetHashCode(y));
    }

    [Fact]
    public void GetHashCode_DifferentValues_ReturnsDifferentHash()
    {
        var x = new Person("Ivan", 22);
        var y = new Person("Judy", 22);

        Assert.NotEqual(_comparer.GetHashCode(x), _comparer.GetHashCode(y));
    }

    [Fact]
    public void GetHashCode_Null_ReturnsZero()
    {
        Assert.Equal(0, _comparer.GetHashCode(null!));
    }
}
