using Raindrop.EventBus.Abstractions;
using Raindrop.EventBus.RabbitMQ;

namespace Raindrop.AppConfiguration.Tests;

public class RabbitMqEventRouteResolverTests
{
    [Fact]
    public void Resolve_EventWithoutRabbitMqAttribute_ReturnsEmpty()
    {
        var resolver = new RabbitMqEventRouteResolver();

        var routes = resolver.Resolve(typeof(EventWithoutAttribute));

        Assert.Empty(routes);
    }

    [Fact]
    public void Resolve_EventWithRabbitMqAttribute_ReturnsRoute()
    {
        var resolver = new RabbitMqEventRouteResolver();

        var routes = resolver.Resolve(typeof(RabbitMqAttributedEvent));

        var route = Assert.Single(routes);
        Assert.IsType<RabbitMqEventAttribute>(route.Metadata);
    }

    [Fact]
    public void Resolve_EventWithMultipleAttributes_ReturnsOnlyRabbitMqRoute()
    {
        var resolver = new RabbitMqEventRouteResolver();

        var routes = resolver.Resolve(typeof(MultiRouteEvent));

        var route = Assert.Single(routes);
        Assert.IsType<RabbitMqEventAttribute>(route.Metadata);
    }

    private sealed record EventWithoutAttribute;

    [RabbitMqEvent("test", "test.route")]
    private sealed record RabbitMqAttributedEvent;

    [LocalEvent]
    [RabbitMqEvent("multi", "multi.route")]
    private sealed record MultiRouteEvent;
}
