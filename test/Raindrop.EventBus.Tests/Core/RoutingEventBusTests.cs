using Raindrop.Data.Events;
using Raindrop.EventBus.Abstractions;
using Raindrop.EventBus.Core;

namespace Raindrop.AppConfiguration.Tests;

public class RoutingEventBusTests
{
    [Fact]
    public async Task PublishAsync_SingleTransport_DispatchesToMatchingTransport()
    {
        var transport = new RecordingTransport(
            new LocalEventAttribute());
        var routeResolver = new StubRouteResolver(new LocalEventAttribute());
        var eventBus = new RoutingEventBus([routeResolver], [transport]);

        await eventBus.PublishAsync(new SimpleEvent());

        Assert.Single(transport.Received);
    }

    [Fact]
    public async Task PublishAsync_MultipleTransports_DispatchesToMatchingOnly()
    {
        var local = new RecordingTransport(new LocalEventAttribute());
        var rabbit = new RecordingTransport(new RabbitMqEventAttributeStub());

        var routeResolver = new StubRouteResolver(new LocalEventAttribute());
        var eventBus = new RoutingEventBus([routeResolver], [local, rabbit]);

        await eventBus.PublishAsync(new SimpleEvent());

        Assert.Single(local.Received);
        Assert.Empty(rabbit.Received);
    }

    [Fact]
    public async Task PublishAsync_NoMatchingTransport_ThrowsClearException()
    {
        var transport = new RecordingTransport(new LocalEventAttribute());
        var routeResolver = new StubRouteResolver(new RabbitMqEventAttributeStub());
        var eventBus = new RoutingEventBus([routeResolver], [transport]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => eventBus.PublishAsync(new SimpleEvent()));

        Assert.Contains("没有找到", exception.Message);
    }

    [Fact]
    public async Task PublishAsync_MultipleRoutes_DispatchesToEachTransport()
    {
        var local = new RecordingTransport(new LocalEventAttribute());
        var rabbit = new RecordingTransport(new RabbitMqEventAttributeStub());
        var routeResolver = new StubRouteResolver(
            new LocalEventAttribute(),
            new RabbitMqEventAttributeStub());
        var eventBus = new RoutingEventBus([routeResolver], [local, rabbit]);

        await eventBus.PublishAsync(new SimpleEvent());

        Assert.Single(local.Received);
        Assert.Single(rabbit.Received);
    }

    private sealed record SimpleEvent : IEvent;

    private sealed class RabbitMqEventAttributeStub : EventRouteAttribute;

    private sealed class RecordingTransport : IEventTransport
    {
        private readonly EventRouteAttribute _routeToMatch;

        public RecordingTransport(EventRouteAttribute routeToMatch)
        {
            _routeToMatch = routeToMatch;
        }

        public List<object> Received { get; } = [];

        public bool CanHandle(EventRoute route)
        {
            return route.Metadata.GetType() == _routeToMatch.GetType();
        }

        public Task PublishAsync<TEvent>(
            TEvent eventData,
            EventRoute route,
            CancellationToken cancellationToken = default)
            where TEvent : IEvent
        {
            Received.Add(eventData!);
            return Task.CompletedTask;
        }
    }

    private sealed class StubRouteResolver : IEventRouteResolver
    {
        private readonly EventRouteAttribute[] _attributes;

        public StubRouteResolver(params EventRouteAttribute[] attributes)
        {
            _attributes = attributes;
        }

        public IReadOnlyCollection<EventRoute> Resolve(Type eventType)
        {
            return _attributes
                .Select(attribute => new EventRoute(attribute))
                .ToArray();
        }
    }
}
