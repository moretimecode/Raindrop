namespace Microsoft.Extensions.Configuration;

/// <summary>
/// 基于委托的实体映射器。
/// </summary>
public class DelegateEntityMapper<TEntity, TConfig> : IEntityMapper<TEntity, TConfig>
    where TEntity : class
    where TConfig : class
{
    private readonly Func<List<TEntity>, TConfig> _mapperFunc;

    public DelegateEntityMapper(Func<List<TEntity>, TConfig> mapperFunc)
    {
        _mapperFunc = mapperFunc ?? throw new ArgumentNullException(nameof(mapperFunc));
    }

    public TConfig Map(List<TEntity> entities)
    {
        return _mapperFunc(entities);
    }
}
