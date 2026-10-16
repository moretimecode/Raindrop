using System.Text.Json;
using Raindrop.EventBus.Abstractions;
using Raindrop.Data.Events;
using Raindrop.EventBus.RabbitMQ;

namespace Raindrop.EventBus.Tests.RabbitMQ;

public sealed class RabbitMqOutboxTargetTests
{
    [Fact]
    public void CaptureTarget_FreezesPublishAddressWithoutOpeningConnection()
    {
        var connection = new ForbiddenConnection();
        var transport = new RabbitMqEventTransport(connection);
        var target = transport.CaptureTarget(new EventRoute(new RabbitMqEventAttribute("orders", "created") { Queue = "consumer-specific" }));
        Assert.Equal("rabbitmq", target.Transport);
        Assert.Equal("1", target.Version);
        using var json = JsonDocument.Parse(target.TargetJson);
        Assert.Equal("orders", json.RootElement.GetProperty("exchange").GetString());
        Assert.Equal("created", json.RootElement.GetProperty("routingKey").GetString());
        Assert.False(json.RootElement.TryGetProperty("queue", out _));
        Assert.Equal(0, connection.Calls);
    }
    [Fact]
    public void LegacyTransport_WithoutSnapshot_ExplicitlyRejectsOutboxOnly()
    {
        IEventTransport legacy = new LegacyTransport();
        Assert.Throws<NotSupportedException>(() => legacy.CaptureTarget(new EventRoute(new LocalEventAttribute())));
    }
    private sealed class LegacyTransport : IEventTransport
    {
        public bool CanHandle(EventRoute route) => true;
        public Task PublishAsync<TEvent>(TEvent data, EventRoute route, CancellationToken cancellationToken = default) where TEvent : IEvent
            => Task.CompletedTask;
    }
    private sealed class ForbiddenConnection : IRabbitMqConnection
    {
        public int Calls { get; private set; }
        public Task<IRabbitMqChannel> CreateChannelAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            throw new InvalidOperationException("登记期间不能连接 MQ");
        }
    }
}
