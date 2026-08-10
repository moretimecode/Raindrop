namespace Raindrop.EventBus.RabbitMQ;

/// <summary>
/// RabbitMQ 消费重试策略。死信统一走全局 raindrop.dlx。
/// </summary>
public sealed record RabbitMqDeadLetterRetryPolicy(
    int MaxRetryCount,
    TimeSpan InitialDelay,
    TimeSpan DelayIncrement,
    TimeSpan MaxDelay)
{
    public bool ShouldDeadLetter(int failedCount)
    {
        return failedCount >= MaxRetryCount;
    }

    public TimeSpan GetRetryDelay(int failedCount)
    {
        if (failedCount <= 0 || ShouldDeadLetter(failedCount))
            return TimeSpan.Zero;

        var delay = InitialDelay + TimeSpan.FromMilliseconds(
            DelayIncrement.TotalMilliseconds * (failedCount - 1));

        return delay <= MaxDelay ? delay : MaxDelay;
    }
}
