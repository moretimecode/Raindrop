namespace Raindrop.EventBus.Abstractions;

public enum ConsumerIdempotencyBeginResult
{
    Started = 0,

    Processing = 1,

    Succeeded = 2
}
