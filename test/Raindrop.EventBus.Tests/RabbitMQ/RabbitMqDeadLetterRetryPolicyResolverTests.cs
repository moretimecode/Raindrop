using Microsoft.Extensions.DependencyInjection;
using Raindrop.EventBus.Abstractions;
using Raindrop.EventBus.Core;
using Raindrop.EventBus.RabbitMQ;

namespace Raindrop.AppConfiguration.Tests;

public class RabbitMqDeadLetterRetryPolicyResolverTests
{
    [Fact]
    public void Resolve_WhenNoAttribute_UsesOptions()
    {
        var resolver = new RabbitMqDeadLetterRetryPolicyResolver(new RabbitMqDeadLetterRetryOptions
        {
            MaxRetryCount = 5,
            InitialDelay = TimeSpan.FromSeconds(10),
            DelayIncrement = TimeSpan.FromSeconds(2),
            MaxDelay = TimeSpan.FromSeconds(30)
        });

        var policy = resolver.Resolve(typeof(NoRetryHandler));

        Assert.Equal(5, policy.MaxRetryCount);
        Assert.Equal(TimeSpan.FromSeconds(10), policy.InitialDelay);
        Assert.Equal(TimeSpan.FromSeconds(2), policy.DelayIncrement);
        Assert.Equal(TimeSpan.FromSeconds(30), policy.MaxDelay);
    }

    [Fact]
    public void Resolve_WhenAttributeOverrides_UsesAttributeValues()
    {
        var resolver = new RabbitMqDeadLetterRetryPolicyResolver(new RabbitMqDeadLetterRetryOptions
        {
            MaxRetryCount = 5
        });

        var policy = resolver.Resolve(typeof(CustomRetryHandler));

        Assert.Equal(4, policy.MaxRetryCount);
        Assert.Equal(TimeSpan.FromSeconds(3), policy.InitialDelay);
        Assert.Equal(TimeSpan.FromSeconds(4), policy.DelayIncrement);
        Assert.Equal(TimeSpan.FromSeconds(30), policy.MaxDelay);
    }

    [Fact]
    public void Resolve_HandlerWithPartialAttribute_UsesAttributeOnly()
    {
        var resolver = new RabbitMqDeadLetterRetryPolicyResolver(new RabbitMqDeadLetterRetryOptions
        {
            MaxRetryCount = 3,
            InitialDelay = TimeSpan.FromSeconds(5)
        });

        var policy = resolver.Resolve(typeof(PartialRetryHandler));

        Assert.Equal(2, policy.MaxRetryCount);
    }

    [Fact]
    public void ShouldDeadLetter_WhenRetryCountReached_ReturnsTrue()
    {
        var policy = new RabbitMqDeadLetterRetryPolicy(3, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero);
        Assert.False(policy.ShouldDeadLetter(2));
        Assert.True(policy.ShouldDeadLetter(3));
    }

    [Fact]
    public void GetRetryDelay_ReturnsCorrectProgressiveDelay()
    {
        var policy = new RabbitMqDeadLetterRetryPolicy(
            5,
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(10));

        Assert.Equal(TimeSpan.FromSeconds(2), policy.GetRetryDelay(1));
        Assert.Equal(TimeSpan.FromSeconds(4), policy.GetRetryDelay(2));
        Assert.Equal(TimeSpan.FromSeconds(8), policy.GetRetryDelay(4));
        Assert.Equal(TimeSpan.FromSeconds(0), policy.GetRetryDelay(5)); // dead lettered
    }

    [Fact]
    public void AddRabbitMqDeadLetterRetry_RegistersPolicyResolver()
    {
        var provider = new ServiceCollection()
            .AddRabbitMqDeadLetterRetry(options => options.MaxRetryCount = 2)
            .BuildServiceProvider();

        var resolver = provider.GetRequiredService<IRabbitMqDeadLetterRetryPolicyResolver>();
        Assert.NotNull(resolver);
    }

    [Fact]
    public void AddRabbitMqEventBus_RegistersDeadLetterRetryByDefault()
    {
        var provider = new ServiceCollection()
            .AddRabbitMqEventBus(typeof(RabbitMqDeadLetterRetryPolicyResolverTests).Assembly)
            .BuildServiceProvider();

        var resolver = provider.GetRequiredService<IRabbitMqDeadLetterRetryPolicyResolver>();
        var policy = resolver.Resolve(typeof(NoRetryHandler));

        Assert.Equal(RabbitMqDeadLetterRetryDefaults.MaxRetryCount, policy.MaxRetryCount);
    }

    private sealed class NoRetryHandler;

    [RabbitMqDeadLetterRetry(
        MaxRetryCount = 4,
        InitialDelaySeconds = 3,
        DelayIncrementSeconds = 4,
        MaxDelaySeconds = 30)]
    private sealed class CustomRetryHandler;

    [RabbitMqDeadLetterRetry(MaxRetryCount = 2)]
    private sealed class PartialRetryHandler;
}
