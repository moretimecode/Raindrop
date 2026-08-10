namespace Raindrop.EventBus.RabbitMQ;

public sealed class RabbitMqDeadLetterRetryOptions
{
    public int MaxRetryCount { get; set; } = RabbitMqDeadLetterRetryDefaults.MaxRetryCount;

    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(RabbitMqDeadLetterRetryDefaults.InitialDelaySeconds);

    public TimeSpan DelayIncrement { get; set; } = TimeSpan.FromSeconds(RabbitMqDeadLetterRetryDefaults.DelayIncrementSeconds);

    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(RabbitMqDeadLetterRetryDefaults.MaxDelaySeconds);
}
