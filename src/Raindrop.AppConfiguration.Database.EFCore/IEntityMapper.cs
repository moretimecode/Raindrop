namespace Microsoft.Extensions.Configuration;

/// <summary>
/// 将实体列表映射为配置对象的接口。
/// </summary>
public interface IEntityMapper<TEntity, TConfig>
    where TEntity : class
    where TConfig : class
{
    /// <summary>
    /// 将实体列表映射为配置对象。
    /// </summary>
    TConfig Map(List<TEntity> entities);
}
