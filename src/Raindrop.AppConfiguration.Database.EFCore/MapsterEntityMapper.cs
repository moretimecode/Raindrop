using Mapster;

namespace Microsoft.Extensions.Configuration;

/// <summary>
/// 基于 Mapster 的实体映射器。
/// </summary>
public class MapsterEntityMapper<TEntity, TConfig> : IEntityMapper<TEntity, TConfig>
    where TEntity : class
    where TConfig : class
{
    private readonly TypeAdapterConfig _config;

    /// <summary>
    /// 使用指定的 <see cref="TypeAdapterConfig"/> 创建 Mapster 映射器。
    /// </summary>
    public MapsterEntityMapper(TypeAdapterConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <summary>
    /// 使用全局 TypeAdapterConfig 创建 Mapster 映射器。
    /// </summary>
    public MapsterEntityMapper()
    {
        _config = TypeAdapterConfig.GlobalSettings;
    }

    public TConfig Map(List<TEntity> entities)
    {
        return entities.Adapt<TConfig>(_config);
    }
}
