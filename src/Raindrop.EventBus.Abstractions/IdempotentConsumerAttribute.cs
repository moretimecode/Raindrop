namespace Raindrop.EventBus.Abstractions;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class IdempotentConsumerAttribute : Attribute
{
    public IdempotentConsumerAttribute(params string[] keyProperties)
    {
        KeyProperties = keyProperties;
    }

    public string[] KeyProperties { get; }

    public string? KeyPrefix { get; init; }

    public string Separator { get; init; } = ":";

    public int ExpireSeconds { get; init; } = 86400;

    public IdempotentConflictBehavior ConflictBehavior { get; init; } =
        IdempotentConflictBehavior.Retry;
}
