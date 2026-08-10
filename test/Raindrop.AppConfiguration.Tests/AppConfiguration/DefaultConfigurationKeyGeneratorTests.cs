using Microsoft.Extensions.Configuration;

namespace Raindrop.AppConfiguration.Tests;

/// <summary>
/// DefaultConfigurationKeyGenerator 的单元测试。
/// 验证递归展平、嵌套对象、集合、null 值、简单类型边界。
/// </summary>
public class DefaultConfigurationKeyGeneratorTests
{
    private const string Section = "TestSection";

    [Fact]
    public void GenerateKeys_SimpleProperties_FlattensCorrectly()
    {
        var generator = new DefaultConfigurationKeyGenerator(Section);
        var config = new SimpleConfig
        {
            Name = "test",
            Count = 42,
            Enabled = true
        };

        var result = generator.GenerateKeys(config);

        Assert.Equal("test", result[$"{Section}:Name"]);
        Assert.Equal("42", result[$"{Section}:Count"]);
        Assert.Equal("True", result[$"{Section}:Enabled"]);
    }

    [Fact]
    public void GenerateKeys_NestedObject_RecursivelyFlattens()
    {
        var generator = new DefaultConfigurationKeyGenerator(Section);
        var config = new ParentConfig
        {
            Title = "parent",
            Child = new SimpleConfig
            {
                Name = "child",
                Count = 7,
                Enabled = false
            }
        };

        var result = generator.GenerateKeys(config);

        Assert.Equal("parent", result[$"{Section}:Title"]);
        Assert.Equal("child", result[$"{Section}:Child:Name"]);
        Assert.Equal("7", result[$"{Section}:Child:Count"]);
        Assert.Equal("False", result[$"{Section}:Child:Enabled"]);
    }

    [Fact]
    public void GenerateKeys_NullProperty_WritesNull()
    {
        var generator = new DefaultConfigurationKeyGenerator(Section);
        var config = new ParentConfig
        {
            Title = "only",
            Child = null
        };

        var result = generator.GenerateKeys(config);

        Assert.Equal("only", result[$"{Section}:Title"]);
        Assert.True(result.ContainsKey($"{Section}:Child"));
        Assert.Null(result[$"{Section}:Child"]);
    }

    [Fact]
    public void GenerateKeys_NullObject_ReturnsEmptyDictionary()
    {
        var generator = new DefaultConfigurationKeyGenerator(Section);

        var result = generator.GenerateKeys<SimpleConfig>(null!);

        Assert.Empty(result);
    }

    [Fact]
    public void GenerateKeys_ListProperty_FlattensWithIndex()
    {
        var generator = new DefaultConfigurationKeyGenerator(Section);
        var config = new CollectionConfig
        {
            Tags = ["alpha", "beta", "gamma"]
        };

        var result = generator.GenerateKeys(config);

        Assert.Equal("alpha", result[$"{Section}:Tags:0"]);
        Assert.Equal("beta", result[$"{Section}:Tags:1"]);
        Assert.Equal("gamma", result[$"{Section}:Tags:2"]);
        Assert.Equal(3, result.Keys.Count(k => k.StartsWith($"{Section}:Tags:")));
    }

    [Fact]
    public void GenerateKeys_NestedList_FlattensWithCompoundIndex()
    {
        var generator = new DefaultConfigurationKeyGenerator(Section);
        var config = new NestedCollectionConfig
        {
            Items =
            [
                new SimpleConfig { Name = "a", Count = 1, Enabled = true },
                new SimpleConfig { Name = "b", Count = 2, Enabled = false }
            ]
        };

        var result = generator.GenerateKeys(config);

        Assert.Equal("a", result[$"{Section}:Items:0:Name"]);
        Assert.Equal("1", result[$"{Section}:Items:0:Count"]);
        Assert.Equal("True", result[$"{Section}:Items:0:Enabled"]);
        Assert.Equal("b", result[$"{Section}:Items:1:Name"]);
        Assert.Equal("2", result[$"{Section}:Items:1:Count"]);
        Assert.Equal("False", result[$"{Section}:Items:1:Enabled"]);
    }

    [Fact]
    public void GenerateKeys_DateTime_FormatsAsString()
    {
        var generator = new DefaultConfigurationKeyGenerator(Section);
        var dt = new DateTime(2025, 3, 15, 10, 30, 0);
        var config = new DateTimeConfig { Timestamp = dt };

        var result = generator.GenerateKeys(config);

        Assert.Equal(dt.ToString(), result[$"{Section}:Timestamp"]);
    }

    [Fact]
    public void GenerateKeys_Guid_FormatsAsString()
    {
        var generator = new DefaultConfigurationKeyGenerator(Section);
        var id = Guid.NewGuid();
        var config = new GuidConfig { Id = id };

        var result = generator.GenerateKeys(config);

        Assert.Equal(id.ToString(), result[$"{Section}:Id"]);
    }

    [Fact]
    public void GenerateKeys_NullableInt_WithValue_FormatsAsString()
    {
        var generator = new DefaultConfigurationKeyGenerator(Section);
        var config = new NullableConfig { Optional = 100 };

        var result = generator.GenerateKeys(config);

        Assert.Equal("100", result[$"{Section}:Optional"]);
    }

    [Fact]
    public void GenerateKeys_NullableInt_Null_WritesNull()
    {
        var generator = new DefaultConfigurationKeyGenerator(Section);
        var config = new NullableConfig { Optional = null };

        var result = generator.GenerateKeys(config);

        Assert.Null(result[$"{Section}:Optional"]);
    }

    [Fact]
    public void GenerateKeys_Constructor_ThrowsOnNullSectionName()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new DefaultConfigurationKeyGenerator(null!));
    }

    [Fact]
    public void GenerateKeys_EmptyObject_ReturnsEmptyDictionary()
    {
        var generator = new DefaultConfigurationKeyGenerator(Section);
        var config = new EmptyConfig();

        var result = generator.GenerateKeys(config);

        Assert.Empty(result);
    }

    [Fact]
    public void GenerateKeys_CaseInsensitiveKeys()
    {
        var generator = new DefaultConfigurationKeyGenerator(Section);
        var config = new SimpleConfig { Name = "case-test", Count = 1, Enabled = false };

        var result = generator.GenerateKeys(config);

        // 验证字典使用 OrdinalIgnoreCase 比较器
        Assert.True(result.ContainsKey($"{Section}:name"));
        Assert.Equal("case-test", result[$"{Section}:NAME"]);
    }

    // ===================================================================
    //  测试用配置类型
    // ===================================================================

    private sealed class SimpleConfig
    {
        public string Name { get; set; } = string.Empty;
        public int Count { get; set; }
        public bool Enabled { get; set; }
    }

    private sealed class ParentConfig
    {
        public string Title { get; set; } = string.Empty;
        public SimpleConfig? Child { get; set; }
    }

    private sealed class CollectionConfig
    {
        public List<string> Tags { get; set; } = [];
    }

    private sealed class NestedCollectionConfig
    {
        public List<SimpleConfig> Items { get; set; } = [];
    }

    private sealed class DateTimeConfig
    {
        public DateTime Timestamp { get; set; }
    }

    private sealed class GuidConfig
    {
        public Guid Id { get; set; }
    }

    private sealed class NullableConfig
    {
        public int? Optional { get; set; }
    }

    private sealed class EmptyConfig
    {
    }
}
