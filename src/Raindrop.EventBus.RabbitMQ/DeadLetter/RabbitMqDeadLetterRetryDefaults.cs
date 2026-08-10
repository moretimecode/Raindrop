namespace Raindrop.EventBus.RabbitMQ;

public static class RabbitMqDeadLetterRetryDefaults
{
    public const int MaxRetryCount = 3;

    public const int InitialDelaySeconds = 2;

    public const int DelayIncrementSeconds = 2;

    public const int MaxDelaySeconds = 300;
}
