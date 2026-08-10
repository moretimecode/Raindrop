using Raindrop.Data.Events;
using Raindrop.EventBus.Abstractions;
using Raindrop.EventBus.Core;

namespace Raindrop.AppConfiguration.Tests;

public class ConsumerIdempotencyKeyResolverTests
{
    [Fact]
    public void Resolve_WithSingleProperty_BuildsBusinessKey()
    {
        var resolver = new PropertyPathConsumerIdempotencyKeyResolver();
        var attribute = new IdempotentConsumerAttribute(nameof(OrderCreatedEvent.OrderId))
        {
            KeyPrefix = "order-created"
        };

        var key = resolver.Resolve(
            new OrderCreatedEvent(1001, new OrderCustomer("tenant-a")),
            typeof(OrderCreatedHandler),
            attribute);

        Assert.Equal("order-created:1001", key);
    }

    [Fact]
    public void Resolve_WithNestedProperty_BuildsBusinessKey()
    {
        var resolver = new PropertyPathConsumerIdempotencyKeyResolver();
        var attribute = new IdempotentConsumerAttribute("Customer.TenantId", nameof(OrderCreatedEvent.OrderId))
        {
            KeyPrefix = "order-created"
        };

        var key = resolver.Resolve(
            new OrderCreatedEvent(1001, new OrderCustomer("tenant-a")),
            typeof(OrderCreatedHandler),
            attribute);

        Assert.Equal("order-created:tenant-a:1001", key);
    }

    [Fact]
    public void Resolve_WithoutKeyProperties_ThrowsClearException()
    {
        var resolver = new PropertyPathConsumerIdempotencyKeyResolver();
        var attribute = new IdempotentConsumerAttribute
        {
            KeyPrefix = "order-created"
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            resolver.Resolve(new OrderCreatedEvent(1001, new OrderCustomer("tenant-a")), typeof(OrderCreatedHandler), attribute));

        Assert.Contains("至少需要一个 Key 属性", exception.Message);
    }

    private sealed record OrderCreatedEvent(long OrderId, OrderCustomer Customer) : IEvent;

    private sealed record OrderCustomer(string TenantId);

    private sealed class OrderCreatedHandler;
}
