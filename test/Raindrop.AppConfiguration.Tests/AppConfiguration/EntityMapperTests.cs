using Mapster;
using Microsoft.Extensions.Configuration;

namespace Raindrop.AppConfiguration.Tests;

/// <summary>
/// DelegateEntityMapper 和 MapsterEntityMapper 的单元测试。
/// </summary>
public class EntityMapperTests
{
    // ===================================================================
    //  测试用类型
    // ===================================================================

    private sealed record TestEntity(string Name, int Value);

    private sealed class TestConfig
    {
        public string Name { get; set; } = string.Empty;
        public int Value { get; set; }
    }

    // ===================================================================
    //  DelegateEntityMapper
    // ===================================================================

    [Fact]
    public void DelegateEntityMapper_Map_InvokesDelegate()
    {
        var entities = new List<TestEntity>
        {
            new("alpha", 1),
            new("beta", 2)
        };

        var mapper = new DelegateEntityMapper<TestEntity, List<TestEntity>>(rows => rows);

        var result = mapper.Map(entities);

        Assert.Same(entities, result);
    }

    [Fact]
    public void DelegateEntityMapper_Constructor_ThrowsOnNullDelegate()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new DelegateEntityMapper<TestEntity, TestConfig>(null!));
    }

    [Fact]
    public void DelegateEntityMapper_Map_TransformsEntities()
    {
        var entities = new List<TestEntity> { new("gamma", 42) };

        var mapper = new DelegateEntityMapper<TestEntity, TestConfig>(rows =>
        {
            var row = rows.Single();
            return new TestConfig { Name = row.Name, Value = row.Value };
        });

        var result = mapper.Map(entities);

        Assert.Equal("gamma", result.Name);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void DelegateEntityMapper_Map_WithEmptyList_PassesToDelegate()
    {
        var invoked = false;
        var mapper = new DelegateEntityMapper<TestEntity, TestConfig>(rows =>
        {
            invoked = true;
            Assert.Empty(rows);
            return new TestConfig();
        });

        var result = mapper.Map([]);

        Assert.True(invoked);
        Assert.NotNull(result);
    }

    // ===================================================================
    //  MapsterEntityMapper
    // ===================================================================

    [Fact]
    public void MapsterEntityMapper_DefaultCtor_UsesGlobalConfig()
    {
        var entities = new List<TestEntity> { new("delta", 99) };

        // 配置全局映射：List<TestEntity> → TestConfig
        TypeAdapterConfig<List<TestEntity>, TestConfig>.NewConfig()
            .Map(dest => dest.Name, src => src[0].Name)
            .Map(dest => dest.Value, src => src[0].Value);

        var mapper = new MapsterEntityMapper<TestEntity, TestConfig>();

        var result = mapper.Map(entities);

        Assert.Equal("delta", result.Name);
        Assert.Equal(99, result.Value);
    }

    [Fact]
    public void MapsterEntityMapper_CustomConfigCtor_UsesProvidedConfig()
    {
        var config = new TypeAdapterConfig();
        config.NewConfig<List<TestEntity>, TestConfig>()
            .Map(dest => dest.Name, src => src[0].Name + "-mapped")
            .Map(dest => dest.Value, src => src[0].Value * 10);

        var entities = new List<TestEntity> { new("epsilon", 3) };

        var mapper = new MapsterEntityMapper<TestEntity, TestConfig>(config);

        var result = mapper.Map(entities);

        Assert.Equal("epsilon-mapped", result.Name);
        Assert.Equal(30, result.Value);
    }

    [Fact]
    public void MapsterEntityMapper_Constructor_ThrowsOnNullConfig()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new MapsterEntityMapper<TestEntity, TestConfig>(null!));
    }

    [Fact]
    public void MapsterEntityMapper_Map_WithMultipleEntities()
    {
        var entities = new List<TestEntity>
        {
            new("first", 1),
            new("second", 2)
        };

        var config = new TypeAdapterConfig();
        config.NewConfig<List<TestEntity>, TestConfig>()
            .Map(dest => dest.Name, src => src[0].Name)
            .Map(dest => dest.Value, src => src.Sum(e => e.Value));

        var mapper = new MapsterEntityMapper<TestEntity, TestConfig>(config);

        var result = mapper.Map(entities);

        Assert.Equal("first", result.Name);
        Assert.Equal(3, result.Value);
    }
}
