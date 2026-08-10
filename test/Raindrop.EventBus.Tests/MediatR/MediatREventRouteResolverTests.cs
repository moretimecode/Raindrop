using Raindrop.EventBus.Abstractions;
using Raindrop.EventBus.MediatR;
using Raindrop.EventBus.RabbitMQ;

namespace Raindrop.AppConfiguration.Tests;

public class MediatREventRouteResolverTests
{
    [Fact]
    public void Resolve_EventWithoutAttributes_ReturnsLocalEventRoute()
    {
        var resolver = new MediatREventRouteResolver();

        var routes = resolver.Resolve(typeof(EventWithoutAttribute));

        var route = Assert.Single(routes);
        Assert.IsType<LocalEventAttribute>(route.Metadata);
    }

    [Fact]
    public void Resolve_EventWithLocalEventAttribute_ReturnsLocalEventRoute()
    {
        var resolver = new MediatREventRouteResolver();

        var routes = resolver.Resolve(typeof(LocalEventOnly));

        var route = Assert.Single(routes);
        Assert.IsType<LocalEventAttribute>(route.Metadata);
    }

    [Fact]
    public void Resolve_EventWithRabbitMqEventOnly_ReturnsEmpty()
    {
        var resolver = new MediatREventRouteResolver();

        var routes = resolver.Resolve(typeof(RabbitMqEventOnly));

        Assert.Empty(routes);
    }

    [Fact]
    public void Resolve_EventWithBothAttributes_ReturnsOnlyLocalEvent()
    {
        var resolver = new MediatREventRouteResolver();

        var routes = resolver.Resolve(typeof(LocalAndRabbitMq));

        var route = Assert.Single(routes);
        Assert.IsType<LocalEventAttribute>(route.Metadata);
    }

    [Fact]
    public void Resolve_EventWithNonLocalEventAttributeOnly_ReturnsEmpty()
    {
        var resolver = new MediatREventRouteResolver();

        var routes = resolver.Resolve(typeof(CustomRouteOnly));

        Assert.Empty(routes);
    }

    private sealed record EventWithoutAttribute;

    [LocalEvent]
    private sealed record LocalEventOnly;

    [RabbitMqEvent("x", "r")]
    private sealed record RabbitMqEventOnly;

    [LocalEvent]
    [RabbitMqEvent("x", "r")]
    private sealed record LocalAndRabbitMq;

    [CustomEventRoute]
    private sealed record CustomRouteOnly;

    private sealed class CustomEventRouteAttribute : EventRouteAttribute;
}
