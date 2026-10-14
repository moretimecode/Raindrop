using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using Raindrop.Data.Events;
using Raindrop.EventBus.Abstractions;
using Raindrop.EventBus.RabbitMQ;

namespace Raindrop.AppConfiguration.Tests;

public class RabbitMqHostedServiceTests
{
    [Fact]
    public async Task StartAsync_ScansHandlersAndDeclaresTopologyPerGroup()
    {
        var assembly = typeof(OrderCreatedEvent).Assembly;

        var declarer = new StubTopologyDeclarer();
        var connection = new StubConnection();
        var service = new RabbitMqHostedService(
            [assembly],
            connection,
            declarer,
            new StubExecutor(),
            new RabbitMqDeadLetterRetryPolicyResolver(new RabbitMqDeadLetterRetryOptions()));

        await service.ExecuteTaskAsync(CancellationToken.None);

        Assert.Contains(declarer.DeclaredGroups,
            g => g.Exchange == "order" && g.RoutingKey == "order.created");
        Assert.Contains(declarer.DeclaredGroups,
            g => g.Exchange == "order" && g.RoutingKey == "order.updated");
        Assert.Contains(declarer.DeclaredGroups,
            g => g.Exchange == "payment" && g.RoutingKey == "payment.succeeded");
    }

    [Fact]
    public async Task StartAsync_ExcludesHandlersWithoutRabbitMqEvent()
    {
        var declarer = new StubTopologyDeclarer();
        var connection = new StubConnection();
        var service = new RabbitMqHostedService(
            [typeof(OrderCreatedEvent).Assembly],
            connection,
            declarer,
            new StubExecutor(),
            new RabbitMqDeadLetterRetryPolicyResolver(new RabbitMqDeadLetterRetryOptions()));

        await service.ExecuteTaskAsync(CancellationToken.None);

        Assert.DoesNotContain(declarer.DeclaredGroups,
            g => g.RoutingKey == "local.only");
    }

    [Fact]
    public async Task StartAsync_EventsWithoutHandlers_AreNotDeclared()
    {
        var declarer = new StubTopologyDeclarer();
        var connection = new StubConnection();
        var service = new RabbitMqHostedService(
            [typeof(OrderCreatedEvent).Assembly],
            connection,
            declarer,
            new StubExecutor(),
            new RabbitMqDeadLetterRetryPolicyResolver(new RabbitMqDeadLetterRetryOptions()));

        await service.ExecuteTaskAsync(CancellationToken.None);

        Assert.DoesNotContain(declarer.DeclaredGroups,
            g => g.RoutingKey == "unhandled.route");
    }

    [Fact]
    public async Task StartAsync_CreatesChannelsPerGroup()
    {
        var declarer = new StubTopologyDeclarer();
        var connection = new StubConnection();
        var service = new RabbitMqHostedService(
            [typeof(OrderCreatedEvent).Assembly],
            connection,
            declarer,
            new StubExecutor(),
            new RabbitMqDeadLetterRetryPolicyResolver(new RabbitMqDeadLetterRetryOptions()));

        await service.ExecuteTaskAsync(CancellationToken.None);

        // 至少包含我们的 3 个组（测试程序集内还有其他 [RabbitMqEvent] handler）
        Assert.True(connection.Channels.Count >= 3);
    }

    [Fact]
    public async Task StopAsync_DisposesAllChannels()
    {
        var declarer = new StubTopologyDeclarer();
        var connection = new StubConnection();
        var service = new RabbitMqHostedService(
            [typeof(OrderCreatedEvent).Assembly],
            connection,
            declarer,
            new StubExecutor(),
            new RabbitMqDeadLetterRetryPolicyResolver(new RabbitMqDeadLetterRetryOptions()));

        await service.ExecuteTaskAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        Assert.All(connection.Channels, c => Assert.True(c.Disposed));
    }

    [Fact]
    public async Task ExecuteTaskAsync_WhenTopologyDeclarationFails_DisposesCreatedChannel()
    {
        var connection = new StubConnection();
        var declarer = new StubTopologyDeclarer { Failure = new InvalidOperationException("模拟拓扑声明失败") };
        var service = new RabbitMqHostedService(
            [typeof(OrderCreatedEvent).Assembly], connection, declarer, new StubExecutor(),
            new RabbitMqDeadLetterRetryPolicyResolver(new RabbitMqDeadLetterRetryOptions()));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExecuteTaskAsync(CancellationToken.None));

        Assert.True(Assert.Single(connection.Channels).Disposed);
    }

    [Fact]
    public async Task DispatchToHandlerAsync_UsesExecutorWithoutCreatingOuterScope()
    {
        var executor = new StubExecutor();
        var service = new RabbitMqHostedService(
            [typeof(OrderCreatedEvent).Assembly],
            new StubConnection(),
            new StubTopologyDeclarer(),
            executor,
            new RabbitMqDeadLetterRetryPolicyResolver(new RabbitMqDeadLetterRetryOptions()));

        var method = typeof(RabbitMqHostedService).GetMethod(
            "DispatchToHandlerAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var task = (Task<RabbitMqHandlerExecutionResult>)method!.Invoke(
            service,
            [new OrderCreatedEvent(), typeof(OrderCreatedHandler), CancellationToken.None])!;

        var result = await task;

        Assert.Equal(RabbitMqHandlerExecutionStatus.Succeeded, result.Status);
        Assert.Equal(1, executor.ExecutionCount);
        Assert.DoesNotContain(
            typeof(RabbitMqHostedService).GetConstructors().SelectMany(ctor => ctor.GetParameters()),
            parameter => parameter.ParameterType == typeof(IServiceScopeFactory));
    }


        [Fact]
        public void EvaluateDispatchResults_AllSucceeded_Acks()
        {
            var results = new List<RabbitMqHandlerExecutionResult>
            {
                new(RabbitMqHandlerExecutionStatus.Succeeded, typeof(OrderCreatedHandler)),
                new(RabbitMqHandlerExecutionStatus.Succeeded, typeof(OrderCreatedHandler))
            };
            var resolver = new RabbitMqDeadLetterRetryPolicyResolver(new RabbitMqDeadLetterRetryOptions());

            var decision = RabbitMqHostedService.EvaluateDispatchResults(results, 0, resolver);

            Assert.Equal(MessageDispatchAction.Ack, decision.Action);
        }

        [Fact]
        public void EvaluateDispatchResults_AllAlreadyProcessed_Acks()
        {
            var results = new List<RabbitMqHandlerExecutionResult>
            {
                new(RabbitMqHandlerExecutionStatus.AlreadyProcessed, typeof(OrderCreatedHandler)),
                new(RabbitMqHandlerExecutionStatus.AlreadyProcessed, typeof(OrderCreatedHandler))
            };
            var resolver = new RabbitMqDeadLetterRetryPolicyResolver(new RabbitMqDeadLetterRetryOptions());

            var decision = RabbitMqHostedService.EvaluateDispatchResults(results, 0, resolver);

            Assert.Equal(MessageDispatchAction.Ack, decision.Action);
        }

        [Fact]
        public void EvaluateDispatchResults_MixedSucceededAndAlreadyProcessed_Acks()
        {
            var results = new List<RabbitMqHandlerExecutionResult>
            {
                new(RabbitMqHandlerExecutionStatus.Succeeded, typeof(OrderCreatedHandler)),
                new(RabbitMqHandlerExecutionStatus.AlreadyProcessed, typeof(OrderCreatedHandler))
            };
            var resolver = new RabbitMqDeadLetterRetryPolicyResolver(new RabbitMqDeadLetterRetryOptions());

            var decision = RabbitMqHostedService.EvaluateDispatchResults(results, 0, resolver);

            Assert.Equal(MessageDispatchAction.Ack, decision.Action);
        }

        [Fact]
        public void EvaluateDispatchResults_OneHandlerFails_Retries()
        {
            var results = new List<RabbitMqHandlerExecutionResult>
            {
                new(RabbitMqHandlerExecutionStatus.Succeeded, typeof(OrderCreatedHandler)),
                new(RabbitMqHandlerExecutionStatus.Retry, typeof(OrderCreatedHandler))
            };
            var resolver = new RabbitMqDeadLetterRetryPolicyResolver(new RabbitMqDeadLetterRetryOptions
            {
                MaxRetryCount = 3,
                InitialDelay = TimeSpan.FromSeconds(1),
                MaxDelay = TimeSpan.FromSeconds(10)
            });

            var decision = RabbitMqHostedService.EvaluateDispatchResults(results, 0, resolver);

            Assert.Equal(MessageDispatchAction.Retry, decision.Action);
            Assert.True(decision.RetryDelay > TimeSpan.Zero);
        }

        [Fact]
        public void EvaluateDispatchResults_AllHandlersFail_Retries()
        {
            var results = new List<RabbitMqHandlerExecutionResult>
            {
                new(RabbitMqHandlerExecutionStatus.Retry, typeof(OrderCreatedHandler)),
                new(RabbitMqHandlerExecutionStatus.Retry, typeof(OrderCreatedHandler))
            };
            var resolver = new RabbitMqDeadLetterRetryPolicyResolver(new RabbitMqDeadLetterRetryOptions
            {
                MaxRetryCount = 3
            });

            var decision = RabbitMqHostedService.EvaluateDispatchResults(results, 0, resolver);

            Assert.Equal(MessageDispatchAction.Retry, decision.Action);
        }

        [Fact]
        public void EvaluateDispatchResults_RetryCountReached_DeadLetters()
        {
            var results = new List<RabbitMqHandlerExecutionResult>
            {
                new(RabbitMqHandlerExecutionStatus.Retry, typeof(OrderCreatedHandler))
            };
            var resolver = new RabbitMqDeadLetterRetryPolicyResolver(new RabbitMqDeadLetterRetryOptions
            {
                MaxRetryCount = 1
            });

            // failedCount = 0, MaxRetryCount = 1 → 0 + 1 >= 1 → DeadLetter
            var decision = RabbitMqHostedService.EvaluateDispatchResults(results, 0, resolver);

            Assert.Equal(MessageDispatchAction.DeadLetter, decision.Action);
        }

        [Fact]
        public void EvaluateDispatchResults_HandlerExplicitlyRequestsDeadLetter_DeadLetters()
        {
            var results = new List<RabbitMqHandlerExecutionResult>
            {
                new(RabbitMqHandlerExecutionStatus.Succeeded, typeof(OrderCreatedHandler)),
                new(RabbitMqHandlerExecutionStatus.DeadLetter, typeof(OrderCreatedHandler))
            };
            var resolver = new RabbitMqDeadLetterRetryPolicyResolver(new RabbitMqDeadLetterRetryOptions());

            var decision = RabbitMqHostedService.EvaluateDispatchResults(results, 0, resolver);

            Assert.Equal(MessageDispatchAction.DeadLetter, decision.Action);
        }

        [Fact]
        public void EvaluateDispatchResults_DeadLetterWinsOverRetry()
        {
            var results = new List<RabbitMqHandlerExecutionResult>
            {
                new(RabbitMqHandlerExecutionStatus.Retry, typeof(OrderCreatedHandler)),
                new(RabbitMqHandlerExecutionStatus.DeadLetter, typeof(OrderCreatedHandler))
            };
            var resolver = new RabbitMqDeadLetterRetryPolicyResolver(new RabbitMqDeadLetterRetryOptions
            {
                MaxRetryCount = 3
            });

            var decision = RabbitMqHostedService.EvaluateDispatchResults(results, 0, resolver);

            Assert.Equal(MessageDispatchAction.DeadLetter, decision.Action);
        }

        [Fact]
        public void EvaluateDispatchResults_MultipleRetryHandlers_UsesMinimumDelay()
        {
            var results = new List<RabbitMqHandlerExecutionResult>
            {
                new(RabbitMqHandlerExecutionStatus.Retry, typeof(OrderCreatedHandler)),
                new(RabbitMqHandlerExecutionStatus.Retry, typeof(OrderCreatedHandler))
            };
            var resolver = new RabbitMqDeadLetterRetryPolicyResolver(new RabbitMqDeadLetterRetryOptions
            {
                MaxRetryCount = 5,
                InitialDelay = TimeSpan.FromSeconds(1),
                DelayIncrement = TimeSpan.FromSeconds(1),
                MaxDelay = TimeSpan.FromSeconds(60)
            });

            var decision = RabbitMqHostedService.EvaluateDispatchResults(results, 0, resolver);

            Assert.Equal(MessageDispatchAction.Retry, decision.Action);
            // InitialDelay = 1s -> GetRetryDelay(1) = 1s
            Assert.Equal(TimeSpan.FromSeconds(1), decision.RetryDelay);
        }

        [Fact]
        public void EvaluateDispatchResults_EmptyResults_Acks()
        {
            var results = new List<RabbitMqHandlerExecutionResult>();
            var resolver = new RabbitMqDeadLetterRetryPolicyResolver(new RabbitMqDeadLetterRetryOptions());

            var decision = RabbitMqHostedService.EvaluateDispatchResults(results, 0, resolver);

            Assert.Equal(MessageDispatchAction.Ack, decision.Action);
        }

        // ===== 测试用的事件和 Handler =====
    // ===== 测试用的事件和 Handler =====

    [RabbitMqEvent("order", "order.created")]
    internal sealed record OrderCreatedEvent : IEvent;

    [RabbitMqEvent("order", "order.updated")]
    internal sealed record OrderUpdatedEvent : IEvent;

    [RabbitMqEvent("payment", "payment.succeeded")]
    internal sealed record PaymentSucceededEvent : IEvent;

    [RabbitMqEvent("orphan", "unhandled.route")]
    internal sealed record UnhandledEvent : IEvent;

    internal sealed record LocalOnlyEvent : IEvent;

    internal sealed class OrderCreatedHandler : IEventHandler<OrderCreatedEvent>
    {
        public Task HandleAsync(OrderCreatedEvent eventData, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    internal sealed class OrderUpdatedHandler : IEventHandler<OrderUpdatedEvent>
    {
        public Task HandleAsync(OrderUpdatedEvent eventData, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    internal sealed class PaymentSucceededHandler : IEventHandler<PaymentSucceededEvent>
    {
        public Task HandleAsync(PaymentSucceededEvent eventData, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    internal sealed class LocalOnlyHandler : IEventHandler<LocalOnlyEvent>
    {
        public Task HandleAsync(LocalOnlyEvent eventData, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    // ===== Stub 类 =====

    private sealed class StubTopologyDeclarer : IRabbitMqTopologyDeclarer
    {
        public List<DeclaredGroup> DeclaredGroups { get; } = [];
        public Exception? Failure { get; init; }

        public Task DeclareAsync(IRabbitMqChannel channel, string exchange, string routingKey, string? queue = null, CancellationToken cancellationToken = default)
        {
            if (Failure is not null) return Task.FromException(Failure);
            DeclaredGroups.Add(new DeclaredGroup(exchange, routingKey));
            return Task.CompletedTask;
        }
    }

    private sealed record DeclaredGroup(string Exchange, string RoutingKey);

    private sealed class StubConnection : IRabbitMqConnection
    {
        public List<StubChannel> Channels { get; } = [];

        public Task<IRabbitMqChannel> CreateChannelAsync(CancellationToken cancellationToken = default)
        {
            var channel = new StubChannel();
            Channels.Add(channel);
            return Task.FromResult<IRabbitMqChannel>(channel);
        }
    }

    private sealed class StubChannel : IRabbitMqChannel
    {
        public bool Disposed { get; private set; }
        public string? ConsumerTag { get; private set; }

        public void Dispose() => Disposed = true;

        public Task ExchangeDeclareAsync(string exchange, string type, bool durable, bool autoDelete, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<QueueDeclareOk> QueueDeclareAsync(
            string queue, bool durable, bool exclusive, bool autoDelete,
            IDictionary<string, object?>? arguments, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new QueueDeclareOk(queue, 0, 0));
        }

        public Task QueueBindAsync(string queue, string exchange, string routingKey, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task BasicQosAsync(uint prefetchSize, ushort prefetchCount, bool global, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<string> BasicConsumeAsync(string queue, bool autoAck, IAsyncBasicConsumer consumer, CancellationToken cancellationToken = default)
        {
            ConsumerTag = $"stub-{queue}";
            return Task.FromResult(ConsumerTag);
        }

        public Task BasicCancelAsync(string consumerTag, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public ValueTask BasicPublishAsync(string exchange, string routingKey, bool mandatory, BasicProperties properties, ReadOnlyMemory<byte> body, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StubScopeFactory : IServiceScopeFactory
    {
        public IServiceScope CreateScope()
        {
            throw new NotImplementedException("单元测试不应触发消息分发。");
        }
    }

    private sealed class CountingScopeFactory : IServiceScopeFactory
    {
        public int CreatedScopes { get; private set; }

        public IServiceScope CreateScope()
        {
            CreatedScopes++;
            return new StubServiceScope();
        }
    }

    private sealed class StubServiceScope : IServiceScope
    {
        public IServiceProvider ServiceProvider { get; } = new ServiceCollection().BuildServiceProvider();

        public void Dispose() { }
    }

    private sealed class StubExecutor : IRabbitMqConsumerExecutor
    {
        public int ExecutionCount { get; private set; }

        public Task<RabbitMqHandlerExecutionResult> ExecuteAsync<TEvent, THandler>(
            TEvent eventData,
            CancellationToken cancellationToken = default)
            where TEvent : IEvent
            where THandler : IEventHandler<TEvent>
        {
            ExecutionCount++;
            return Task.FromResult(new RabbitMqHandlerExecutionResult(
                RabbitMqHandlerExecutionStatus.Succeeded,
                typeof(THandler)));
        }
    }
}
