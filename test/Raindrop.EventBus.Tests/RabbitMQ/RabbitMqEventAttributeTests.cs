using Raindrop.EventBus.Abstractions;
using Raindrop.EventBus.RabbitMQ;

namespace Raindrop.AppConfiguration.Tests;

public class RabbitMqEventAttributeTests
{
    [Fact]
    public void Constructor_WithExchangeAndRoutingKey_CreatesRouteMetadata()
    {
        var attribute = new RabbitMqEventAttribute("orders", "orders.created")
        {
            Queue = "orders.created.queue"
        };

        Assert.Equal("orders", attribute.Exchange);
        Assert.Equal("orders.created", attribute.RoutingKey);
        Assert.Equal("orders.created.queue", attribute.Queue);
    }

    [Theory]
    [InlineData("", "orders.created")]
    [InlineData("orders", "")]
    public void Constructor_WithEmptyRoute_Throws(string exchange, string routingKey)
    {
        Assert.Throws<ArgumentException>(() => new RabbitMqEventAttribute(exchange, routingKey));
    }

    [Fact]
    public void RabbitMqEventTransport_CanHandleOnlyRabbitMqRoute()
    {
        var transport = new RabbitMqEventTransport(null!);

        Assert.True(transport.CanHandle(new EventRoute(new RabbitMqEventAttribute("orders", "orders.created"))));
        Assert.False(transport.CanHandle(new EventRoute(new LocalEventAttribute())));
    }
}
