namespace Raindrop.EventBus.Abstractions;

public enum IdempotentConflictBehavior
{
    Ack = 0,

    Retry = 1,

    Throw = 2
}
