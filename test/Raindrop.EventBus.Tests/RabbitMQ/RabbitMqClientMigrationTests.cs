using System.Reflection;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using Raindrop.EventBus.Abstractions;
using Raindrop.EventBus.RabbitMQ;

namespace Raindrop.AppConfiguration.Tests;

public class RabbitMqClientMigrationTests
{
    [Fact]
    public async Task RetryAsync_PreservesMessageAndAcknowledgesOnlyAfterPublishCompletes()
    {
        var published = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var channel = ClientProxy.Create<IChannel>(out var proxy);
        proxy.PublishCompletion = published.Task;
        var properties = new BasicProperties
        {
            MessageId = "message-42",
            CorrelationId = "request-7",
            ContentType = "application/json",
            Type = "order.created",
            Headers = new Dictionary<string, object?> { ["tenant"] = "rain" }
        };
        var body = Encoding.UTF8.GetBytes("{\"id\":42}");
        using var cancellation = new CancellationTokenSource();
        var message = new RabbitMqReceivedMessage(channel, 42, body, properties, 1, "orders:retry");

        var retry = message.RetryAsync(TimeSpan.FromMilliseconds(250), cancellation.Token);

        Assert.False(retry.IsCompleted);
        Assert.DoesNotContain(proxy.Calls, call => call.Name == "BasicAckAsync");
        var publish = Assert.Single(proxy.Calls, call => call.Name == "BasicPublishAsync");
        Assert.Equal(string.Empty, publish.Arguments[0]);
        Assert.Equal("orders:retry", publish.Arguments[1]);
        var retryProperties = Assert.IsType<BasicProperties>(publish.Arguments[3]);
        Assert.True(retryProperties.Persistent);
        Assert.Equal("message-42", retryProperties.MessageId);
        Assert.Equal("request-7", retryProperties.CorrelationId);
        Assert.Equal("application/json", retryProperties.ContentType);
        Assert.Equal("order.created", retryProperties.Type);
        Assert.Equal("250", retryProperties.Expiration);
        Assert.Equal("rain", retryProperties.Headers!["tenant"]);
        Assert.Equal("2", Encoding.UTF8.GetString(Assert.IsType<byte[]>(retryProperties.Headers[RabbitMqMessageHeaders.FailedCount])));
        Assert.False(properties.Headers.ContainsKey(RabbitMqMessageHeaders.FailedCount));
        Assert.Equal(body, Assert.IsType<ReadOnlyMemory<byte>>(publish.Arguments[4]).ToArray());
        Assert.Equal(cancellation.Token, publish.Arguments[^1]);

        published.SetResult();
        await retry;

        var ack = Assert.Single(proxy.Calls, call => call.Name == "BasicAckAsync");
        Assert.Equal(42UL, ack.Arguments[0]);
        Assert.Equal(false, ack.Arguments[1]);
        Assert.Equal(cancellation.Token, ack.Arguments[^1]);
    }

    [Fact]
    public async Task RetryAsync_WhenPublishingFails_DoesNotAcknowledgeOriginalMessage()
    {
        var channel = ClientProxy.Create<IChannel>(out var proxy);
        proxy.PublishCompletion = Task.FromException(new InvalidOperationException("模拟发布失败"));
        var message = new RabbitMqReceivedMessage(channel, 42, [], new BasicProperties(), 0, "orders:retry");

        await Assert.ThrowsAsync<InvalidOperationException>(() => message.RetryAsync(TimeSpan.FromSeconds(1)));

        Assert.DoesNotContain(proxy.Calls, call => call.Name == "BasicAckAsync");
        await message.DeadLetterAsync();
        var reject = Assert.Single(proxy.Calls, call => call.Name == "BasicRejectAsync");
        Assert.Equal(42UL, reject.Arguments[0]);
        Assert.Equal(false, reject.Arguments[1]);
    }

    [Fact]
    public async Task AckAsync_WithCancelledToken_DoesNotSendAcknowledgement()
    {
        var channel = ClientProxy.Create<IChannel>(out var proxy);
        var message = new RabbitMqReceivedMessage(channel, 42, [], new BasicProperties(), 0, "orders:retry");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => message.AckAsync(cancellation.Token));

        Assert.Empty(proxy.Calls);
    }

    [Fact]
    public async Task ConnectionAdapter_WithExternalConnection_CreatesChannelAndLeavesConnectionOwnedByCaller()
    {
        var connection = ClientProxy.Create<IConnection>(out var proxy);
        proxy.Channel = ClientProxy.Create<IChannel>(out _);
        using var adapter = new RabbitMqConnectionAdapter(connection);
        using var cancellation = new CancellationTokenSource();

        await using var channel = await adapter.CreateChannelAsync(cancellation.Token);
        adapter.Dispose();

        var create = Assert.Single(proxy.Calls, call => call.Name == "CreateChannelAsync");
        Assert.Equal(cancellation.Token, create.Arguments[^1]);
        Assert.DoesNotContain(proxy.Calls, call => call.Name is "Dispose" or "DisposeAsync");
    }

    [Fact]
    public void AddRabbitMqEventBus_ResolvingTransport_DoesNotConnectToBroker()
    {
        using var provider = new ServiceCollection()
            .AddRabbitMqEventBus(options => options.ConnectionString = "amqp://localhost:1",
                typeof(RabbitMqClientMigrationTests).Assembly)
            .BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.Contains(scope.ServiceProvider.GetServices<IEventTransport>(),
            transport => transport is RabbitMqEventTransport);
    }

    [Fact]
    public async Task ConnectionAdapter_WithConcurrentRequests_ReusesLazyConnectionAndDisposesItOnce()
    {
        var channel = ClientProxy.Create<IChannel>(out _);
        var connection = ClientProxy.Create<IConnection>(out var connectionProxy);
        connectionProxy.Channel = channel;
        var factory = ClientProxy.Create<IConnectionFactory>(out var factoryProxy);
        var connected = new TaskCompletionSource<IConnection>(TaskCreationOptions.RunContinuationsAsynchronously);
        factoryProxy.ConnectionCompletion = connected.Task;
        var adapter = new RabbitMqConnectionAdapter(factory);
        Assert.Empty(factoryProxy.Calls);

        var first = adapter.CreateChannelAsync();
        var second = adapter.CreateChannelAsync();
        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);
        connected.SetResult(connection);
        var channels = await Task.WhenAll(first, second);

        Assert.Single(factoryProxy.Calls, call => call.Name == "CreateConnectionAsync");
        Assert.Equal(2, connectionProxy.Calls.Count(call => call.Name == "CreateChannelAsync"));
        foreach (var createdChannel in channels) await createdChannel.DisposeAsync();
        await adapter.DisposeAsync();
        await adapter.DisposeAsync();
        Assert.Single(connectionProxy.Calls, call => call.Name == "DisposeAsync");
        await Assert.ThrowsAsync<ObjectDisposedException>(() => adapter.CreateChannelAsync());
    }

    [Fact]
    public async Task ConnectionAdapter_AfterFailedConnection_CanRetryOnNextRequest()
    {
        var factory = ClientProxy.Create<IConnectionFactory>(out var factoryProxy);
        factoryProxy.ConnectionCompletion = Task.FromException<IConnection>(new InvalidOperationException("模拟连接失败"));
        await using var adapter = new RabbitMqConnectionAdapter(factory);
        await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.CreateChannelAsync());

        var connection = ClientProxy.Create<IConnection>(out var connectionProxy);
        connectionProxy.Channel = ClientProxy.Create<IChannel>(out _);
        factoryProxy.ConnectionCompletion = Task.FromResult(connection);
        await using var channel = await adapter.CreateChannelAsync();

        Assert.Equal(2, factoryProxy.Calls.Count(call => call.Name == "CreateConnectionAsync"));
    }

    [Fact]
    public async Task PublishAsync_PublishesJsonToRouteAndDisposesChannel()
    {
        var connection = ClientProxy.Create<IConnection>(out var connectionProxy);
        connectionProxy.Channel = ClientProxy.Create<IChannel>(out var channelProxy);
        using var adapter = new RabbitMqConnectionAdapter(connection);
        var transport = new RabbitMqEventTransport(adapter);
        using var cancellation = new CancellationTokenSource();

        await transport.PublishAsync(new PublishedEvent(42),
            new EventRoute(new RabbitMqEventAttribute("orders", "order.created")), cancellation.Token);

        var publish = Assert.Single(channelProxy.Calls, call => call.Name == "BasicPublishAsync");
        Assert.Equal("orders", publish.Arguments[0]);
        Assert.Equal("order.created", publish.Arguments[1]);
        Assert.Equal(false, publish.Arguments[2]);
        var properties = Assert.IsType<BasicProperties>(publish.Arguments[3]);
        Assert.True(properties.Persistent);
        Assert.Equal("application/json", properties.ContentType);
        Assert.Equal(typeof(PublishedEvent).AssemblyQualifiedName, properties.Type);
        Assert.Equal("{\"Id\":42}", Encoding.UTF8.GetString(Assert.IsType<ReadOnlyMemory<byte>>(publish.Arguments[4]).Span));
        Assert.Equal(cancellation.Token, publish.Arguments[^1]);
        Assert.Single(channelProxy.Calls, call => call.Name == "DisposeAsync");
    }

    private sealed record PublishedEvent(int Id) : Raindrop.Data.Events.IEvent;

    public class ClientProxy : DispatchProxy
    {
        public List<ClientCall> Calls { get; } = [];
        public Task PublishCompletion { get; set; } = Task.CompletedTask;
        public IChannel? Channel { get; set; }
        public Task<IConnection>? ConnectionCompletion { get; set; }

        public static T Create<T>(out ClientProxy proxy) where T : class
        {
            var client = Create<T, ClientProxy>();
            proxy = (ClientProxy)(object)client;
            return client;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var method = targetMethod!;
            Calls.Add(new ClientCall(method.Name, args ?? []));
            return method.Name switch
            {
                "BasicPublishAsync" => new ValueTask(PublishCompletion),
                "BasicAckAsync" or "BasicRejectAsync" => ValueTask.CompletedTask,
                "CreateChannelAsync" => Task.FromResult(Channel!),
                "CreateConnectionAsync" => ConnectionCompletion,
                "DisposeAsync" => ValueTask.CompletedTask,
                "Dispose" => null,
                _ => throw new NotSupportedException($"测试未配置客户端方法：{method.Name}")
            };
        }
    }

    public sealed record ClientCall(string Name, object?[] Arguments);
}
