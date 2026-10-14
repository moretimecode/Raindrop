using Raindrop.EventBus.RabbitMQ;
using RabbitMQ.Client;

namespace Raindrop.AppConfiguration.Tests;

public class RabbitMqTopologyDeclarerTests
{
    [Fact]
    public async Task Declare_ForExchangeAndRoutingKey_CreatesMainAndDeadLetterExchanges()
    {
        var channel = new StubRabbitMqChannel();
        var declarer = new RabbitMqTopologyDeclarer();

        await declarer.DeclareAsync(channel, "order", "order.created");

        var exchanges = channel.DeclaredExchanges.Select(e => e.Name).ToArray();
        Assert.Contains("order", exchanges);
        Assert.Contains("raindrop.dlx", exchanges);
    }

    [Fact]
    public async Task Declare_ForExchangeAndRoutingKey_DeclaresExchangesAsDirectAndDurable()
    {
        var channel = new StubRabbitMqChannel();
        var declarer = new RabbitMqTopologyDeclarer();

        await declarer.DeclareAsync(channel, "order", "order.created");

        Assert.All(channel.DeclaredExchanges, e =>
        {
            Assert.Equal(ExchangeType.Direct, e.Type);
            Assert.True(e.Durable);
            Assert.False(e.AutoDelete);
        });
    }

    [Fact]
    public async Task Declare_ForExchangeAndRoutingKey_CreatesMainQueueWithDlx()
    {
        var channel = new StubRabbitMqChannel();
        var declarer = new RabbitMqTopologyDeclarer();

        await declarer.DeclareAsync(channel, "order", "order.created");

        var mainQueue = Assert.Single(
            channel.DeclaredQueues,
            q => q.Name == "raindrop:order:order.created");
        Assert.True(mainQueue.Durable);
        Assert.False(mainQueue.Exclusive);
        Assert.False(mainQueue.AutoDelete);
        Assert.Equal("raindrop.dlx", mainQueue.Arguments?["x-dead-letter-exchange"]);
        Assert.Equal("raindrop.dlx", mainQueue.Arguments?["x-dead-letter-routing-key"]);
    }

    [Fact]
    public async Task Declare_ForExchangeAndRoutingKey_CreatesRetryQueueWithDlxToMainExchange()
    {
        var channel = new StubRabbitMqChannel();
        var declarer = new RabbitMqTopologyDeclarer();

        await declarer.DeclareAsync(channel, "order", "order.created");

        var retryQueue = Assert.Single(
            channel.DeclaredQueues,
            q => q.Name == "raindrop:order:order.created:retry");
        Assert.True(retryQueue.Durable);
        Assert.False(retryQueue.Exclusive);
        Assert.False(retryQueue.AutoDelete);
        Assert.Equal("order", retryQueue.Arguments?["x-dead-letter-exchange"]);
        Assert.Equal("order.created", retryQueue.Arguments?["x-dead-letter-routing-key"]);
    }

    [Fact]
    public async Task Declare_ForExchangeAndRoutingKey_CreatesSharedDeadLetterQueue()
    {
        var channel = new StubRabbitMqChannel();
        var declarer = new RabbitMqTopologyDeclarer();

        await declarer.DeclareAsync(channel, "payment", "payment.succeeded");

        var dlq = Assert.Single(
            channel.DeclaredQueues,
            q => q.Name == "raindrop.dlq");
        Assert.True(dlq.Durable);
    }

    [Fact]
    public async Task Declare_ForExchangeAndRoutingKey_BindsQueuesCorrectly()
    {
        var channel = new StubRabbitMqChannel();
        var declarer = new RabbitMqTopologyDeclarer();

        await declarer.DeclareAsync(channel, "order", "order.created");

        Assert.Contains(channel.Bindings,
            b => b.Queue == "raindrop:order:order.created"
                && b.Exchange == "order"
                && b.RoutingKey == "order.created");

        Assert.Contains(channel.Bindings,
            b => b.Queue == "raindrop.dlq"
                && b.Exchange == "raindrop.dlx"
                && b.RoutingKey == "raindrop.dlx");
    }

    [Fact]
    public async Task Declare_DeclaringSameExchangeWithDifferentRoutingKey_CreatesSeparateQueues()
    {
        var channel = new StubRabbitMqChannel();
        var declarer = new RabbitMqTopologyDeclarer();

        await declarer.DeclareAsync(channel, "order", "order.created");
        await declarer.DeclareAsync(channel, "order", "order.updated");

        // 同一个 exchange 声明两次是幂等的（RabbitMQ 保证），两个 routing key 各一个队列
        Assert.Contains(channel.DeclaredQueues,
            q => q.Name == "raindrop:order:order.created");
        Assert.Contains(channel.DeclaredQueues,
            q => q.Name == "raindrop:order:order.updated");
    }

    private sealed class StubRabbitMqChannel : IRabbitMqChannel
    {
        public List<ExchangeDeclaration> DeclaredExchanges { get; } = [];
        public List<QueueDeclaration> DeclaredQueues { get; } = [];
        public List<BindingDeclaration> Bindings { get; } = [];

        public Task ExchangeDeclareAsync(string exchange, string type, bool durable, bool autoDelete, CancellationToken cancellationToken = default)
        {
            DeclaredExchanges.Add(new ExchangeDeclaration(exchange, type, durable, autoDelete));
            return Task.CompletedTask;
        }

        public Task<QueueDeclareOk> QueueDeclareAsync(
            string queue, bool durable, bool exclusive, bool autoDelete,
            IDictionary<string, object?>? arguments, CancellationToken cancellationToken = default)
        {
            DeclaredQueues.Add(new QueueDeclaration(queue, durable, exclusive, autoDelete, arguments));
            return Task.FromResult(new QueueDeclareOk(queue, 0, 0));
        }

        public Task QueueBindAsync(string queue, string exchange, string routingKey, CancellationToken cancellationToken = default)
        {
            Bindings.Add(new BindingDeclaration(queue, exchange, routingKey));
            return Task.CompletedTask;
        }

        public void Dispose() { }

        public Task BasicQosAsync(uint prefetchSize, ushort prefetchCount, bool global, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<string> BasicConsumeAsync(string queue, bool autoAck, IAsyncBasicConsumer consumer, CancellationToken cancellationToken = default)
        {
            return Task.FromResult("stub");
        }

        public Task BasicCancelAsync(string consumerTag, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public ValueTask BasicPublishAsync(string exchange, string routingKey, bool mandatory, BasicProperties properties, ReadOnlyMemory<byte> body, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }

        public sealed record ExchangeDeclaration(string Name, string Type, bool Durable, bool AutoDelete);
        public sealed record QueueDeclaration(string Name, bool Durable, bool Exclusive, bool AutoDelete, IDictionary<string, object?>? Arguments);
        public sealed record BindingDeclaration(string Queue, string Exchange, string RoutingKey);
    }
}
