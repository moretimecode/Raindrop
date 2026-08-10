namespace Raindrop.AppConfiguration;

/// <summary>
/// 配置加载失败时的行为策略。
/// </summary>
public enum ConfigurationLoadFailureBehavior
{
    /// <summary>
    /// 抛出异常，中断启动或刷新。
    /// </summary>
    Throw,

    /// <summary>
    /// 忽略异常。初次加载时保持空配置，刷新时保持当前配置。
    /// </summary>
    Ignore,

    /// <summary>
    /// 保留上一次成功加载的配置值。刷新时适用，初次加载时等同于 Ignore。
    /// </summary>
    KeepLastKnownGood,
}
