using System.Reflection;

namespace Raindrop.EventBus.RabbitMQ;

public sealed class RabbitMqDeadLetterRetryPolicyResolver(
    RabbitMqDeadLetterRetryOptions options) : IRabbitMqDeadLetterRetryPolicyResolver
{
    private readonly RabbitMqDeadLetterRetryOptions _options = options ?? throw new ArgumentNullException(nameof(options));

    public RabbitMqDeadLetterRetryPolicy Resolve(Type handlerType)
    {
        ArgumentNullException.ThrowIfNull(handlerType);

        var attribute = handlerType.GetCustomAttribute<RabbitMqDeadLetterRetryAttribute>();

        var policy = attribute is null
            ? new RabbitMqDeadLetterRetryPolicy(
                _options.MaxRetryCount,
                _options.InitialDelay,
                _options.DelayIncrement,
                _options.MaxDelay)
            : new RabbitMqDeadLetterRetryPolicy(
                attribute.MaxRetryCount,
                TimeSpan.FromSeconds(attribute.InitialDelaySeconds),
                TimeSpan.FromSeconds(attribute.DelayIncrementSeconds),
                TimeSpan.FromSeconds(attribute.MaxDelaySeconds));

        Validate(policy, handlerType);

        return policy;
    }

    private static void Validate(RabbitMqDeadLetterRetryPolicy policy, Type handlerType)
    {
        if (policy.MaxRetryCount < 0)
            throw new InvalidOperationException($"{handlerType.FullName} 的最大重试次数不能小于 0。");

        if (policy.InitialDelay < TimeSpan.Zero)
            throw new InvalidOperationException($"{handlerType.FullName} 的初始重试延迟不能小于 0。");

        if (policy.DelayIncrement < TimeSpan.Zero)
            throw new InvalidOperationException($"{handlerType.FullName} 的重试递增延迟不能小于 0。");

        if (policy.MaxDelay < policy.InitialDelay)
            throw new InvalidOperationException($"{handlerType.FullName} 的最大重试延迟不能小于初始重试延迟。");
    }
}
