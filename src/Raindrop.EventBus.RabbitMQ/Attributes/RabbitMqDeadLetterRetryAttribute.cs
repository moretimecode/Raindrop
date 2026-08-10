namespace Raindrop.EventBus.RabbitMQ;

/// <summary>
/// 声明 RabbitMQ 消费处理器失败后的重试策略。死信统一进入全局 raindrop.dlx。
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class RabbitMqDeadLetterRetryAttribute : Attribute
{
    public int MaxRetryCount { get; init; } = RabbitMqDeadLetterRetryDefaults.MaxRetryCount;

    public int InitialDelaySeconds { get; init; } = RabbitMqDeadLetterRetryDefaults.InitialDelaySeconds;

    public int DelayIncrementSeconds { get; init; } = RabbitMqDeadLetterRetryDefaults.DelayIncrementSeconds;

    public int MaxDelaySeconds { get; init; } = RabbitMqDeadLetterRetryDefaults.MaxDelaySeconds;
}
