using Raindrop.Data.Events;
using Raindrop.EventBus.Abstractions;
using Raindrop.EventBus.RabbitMQ;

namespace Raindrop.AppConfiguration.Tests;

public class RabbitMqEventTransportTests
{
    [Fact]
    public void CanHandle_RabbitMqRoute_ReturnsTrue()
    {
        var transport = new RabbitMqEventTransport(null!);

        var result = transport.CanHandle(
            new EventRoute(new RabbitMqEventAttribute("exchange", "routing")));

        Assert.True(result);
    }

    [Fact]
    public void CanHandle_LocalRoute_ReturnsFalse()
    {
        var transport = new RabbitMqEventTransport(null!);

        var result = transport.CanHandle(
            new EventRoute(new LocalEventAttribute()));

        Assert.False(result);
    }

    [Fact]
    public async Task PublishAsync_NonRabbitMqRoute_ThrowsClearException()
    {
        var transport = new RabbitMqEventTransport(null!);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => transport.PublishAsync<TestEvent>(
                new TestEvent(),
                new EventRoute(new LocalEventAttribute())));

        Assert.Contains("RabbitMQ", exception.Message);
        Assert.Contains("不能处理", exception.Message);
    }

    private sealed record TestEvent : IEvent;
}
