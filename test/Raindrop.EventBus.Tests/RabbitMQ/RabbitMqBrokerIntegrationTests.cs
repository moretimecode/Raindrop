using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Raindrop.Data.Events;
using Raindrop.EventBus.Abstractions;
using Raindrop.EventBus.RabbitMQ;

namespace Raindrop.AppConfiguration.Tests;

[Collection(PostgreSqlCollection.Name)]
public class RabbitMqBrokerIntegrationTests : IClassFixture<RabbitMqTestFixture>
{
    private readonly PostgreSqlTestFixture _postgres;
    private readonly RabbitMqTestFixture _rabbitMq;

    public RabbitMqBrokerIntegrationTests(PostgreSqlTestFixture postgres, RabbitMqTestFixture rabbitMq)
    {
        _postgres = postgres;
        _rabbitMq = rabbitMq;
    }

    [Fact]
    public async Task PublishLocalAndRabbitMqEvents_BothConsumedAndRecordedInPostgres()
    {
        var scenario = NewScenarioName();
        await DeleteScenarioRecordsAsync(scenario);
        await using var topology = await _rabbitMq.CreateTopologyAsync(
            RabbitMqTestRoutes.MainExchange,
            RabbitMqTestRoutes.MainRoutingKey);
        using var provider = CreateServices()
            .AddSingleton(_rabbitMq.Connection)
            .AddMediatRLocalEventBus(typeof(RabbitMqBrokerIntegrationTests).Assembly)
            .AddRabbitMqEventBus(options => options.ConnectionString = RabbitMqTestSettings.ConnectionString,
                typeof(RabbitMqBrokerIntegrationTests).Assembly)
            .BuildServiceProvider();
        await using var consumer = await StartConsumerAsync<RabbitRecordEvent, RabbitRecordHandler>(provider, topology);

        var eventBus = provider.GetRequiredService<IEventBus>();

        await eventBus.PublishAsync(new LocalRecordEvent(scenario, "local"));
        await eventBus.PublishAsync(new RabbitRecordEvent(scenario, "rabbit"));

        await WaitUntilAsync(async () =>
            await CountRecordsAsync(scenario, "local") == 1 &&
            await CountRecordsAsync(scenario, "rabbit") == 1);
    }

    [Fact]
    public async Task RabbitMqConsumer_WhenHandlerFailsOnce_RetriesAndConsumesMessage()
    {
        var scenario = NewScenarioName();
        await DeleteScenarioRecordsAsync(scenario);
        await using var topology = await _rabbitMq.CreateTopologyAsync();
        await topology.VerifyRetryRouteAsync();
        using var provider = CreateServices(options =>
            {
                options.MaxRetryCount = 3;
                options.InitialDelay = TimeSpan.FromMilliseconds(100);
                options.MaxDelay = TimeSpan.FromMilliseconds(100);
            })
            .AddRabbitMqEventBus(options => options.ConnectionString = RabbitMqTestSettings.ConnectionString,
                typeof(RabbitMqBrokerIntegrationTests).Assembly)
            .BuildServiceProvider();
        var consumerErrors = new ConcurrentQueue<string>();
        await using var consumer = await StartConsumerAsync<RetryRecordEvent, RetryThenSuccessHandler>(provider, topology, consumerErrors);

        await topology.PublishAsync(new RetryRecordEvent(scenario, "retry"));

        var retryAttemptCount = 0;
        var retrySuccessCount = 0;
        await WaitUntilAsync(async () =>
            {
                retryAttemptCount = await CountRecordsAsync(scenario, "retry-attempt");
                retrySuccessCount = await CountRecordsAsync(scenario, "retry-success");
                return retryAttemptCount >= 2 && retrySuccessCount >= 1;
            },
            () => $"attempt={retryAttemptCount}, success={retrySuccessCount}, errors={string.Join(" | ", consumerErrors)}");

        Assert.Equal(0u, await topology.GetMessageCountAsync(topology.DeadLetterQueue));
    }

    [Fact]
    public async Task RabbitMqConsumer_WhenRetryCountReached_MovesMessageToDeadLetterQueue()
    {
        var scenario = NewScenarioName();
        await DeleteScenarioRecordsAsync(scenario);
        await using var topology = await _rabbitMq.CreateTopologyAsync();
        using var provider = CreateServices(options =>
            {
                options.MaxRetryCount = 1;
                options.InitialDelay = TimeSpan.FromMilliseconds(50);
                options.MaxDelay = TimeSpan.FromMilliseconds(50);
            })
            .AddRabbitMqEventBus(options => options.ConnectionString = RabbitMqTestSettings.ConnectionString,
                typeof(RabbitMqBrokerIntegrationTests).Assembly)
            .BuildServiceProvider();
        await using var consumer = await StartConsumerAsync<DeadLetterRecordEvent, AlwaysFailingHandler>(provider, topology);

        await topology.PublishAsync(new DeadLetterRecordEvent(scenario, "dead-letter"));

        await WaitUntilAsync(async () => await topology.GetMessageCountAsync(topology.DeadLetterQueue) == 1);

        Assert.Equal(1, await CountRecordsAsync(scenario, "dead-letter-attempt"));
    }

    private IServiceCollection CreateServices(Action<RabbitMqDeadLetterRetryOptions>? configure = null)
    {
        return new ServiceCollection()
            .AddSingleton(_postgres)
            .AddRabbitMqDeadLetterRetry(configure);
    }


    private async Task<RabbitMqConsumerRegistration> StartConsumerAsync<TEvent, THandler>(
        ServiceProvider provider,
        RabbitMqTopology topology,
        ConcurrentQueue<string>? consumerErrors = null)
        where TEvent : IEvent
        where THandler : IEventHandler<TEvent>
    {
        var channel = await _rabbitMq.Connection.CreateChannelAsync();
        await channel.BasicQosAsync(0, 1, false);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, args) =>
        {
            try
            {
                if (args.BasicProperties is null)
                {
                    await channel.BasicRejectAsync(args.DeliveryTag, false);
                    return;
                }

                using var linkedCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var eventData = JsonSerializer.Deserialize<TEvent>(Encoding.UTF8.GetString(args.Body.Span))
                    ?? throw new InvalidOperationException("RabbitMQ 消息反序列化失败。");
                var failedCount = RabbitMqReceivedMessage.ReadFailedCount(args.BasicProperties);
                var message = new RabbitMqReceivedMessage(
                    channel,
                    args.DeliveryTag,
                    args.Body.ToArray(),
                    args.BasicProperties,
                    failedCount,
                    topology.RetryQueue);

                var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
                var executor = new RabbitMqConsumerExecutor(scopeFactory);
                var result = await executor.ExecuteAsync<TEvent, THandler>(eventData, linkedCts.Token);

                if (result.Status is RabbitMqHandlerExecutionStatus.Succeeded or RabbitMqHandlerExecutionStatus.AlreadyProcessed)
                    await message.AckAsync(linkedCts.Token);
                else if (result.Status == RabbitMqHandlerExecutionStatus.Retry)
                {
                    var policyResolver = provider.GetRequiredService<IRabbitMqDeadLetterRetryPolicyResolver>();
                    var policy = policyResolver.Resolve(typeof(THandler));
                    if (policy.ShouldDeadLetter(failedCount + 1))
                        await message.DeadLetterAsync(linkedCts.Token);
                    else
                        await message.RetryAsync(policy.GetRetryDelay(failedCount + 1), linkedCts.Token);
                }
                else
                    await message.DeadLetterAsync(linkedCts.Token);
            }
            catch (Exception ex)
            {
                consumerErrors?.Enqueue(ex.ToString());
            }
        };

        var consumerTag = await channel.BasicConsumeAsync(topology.MainQueue, false, consumer);

        return new RabbitMqConsumerRegistration(channel, consumerTag);
    }

    private async Task<int> CountRecordsAsync(string scenario, string source)
    {
        await using var connection = _postgres.CreateConnection();
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            select count(*)
            from {PostgreSqlTestFixture.SchemaName}.event_consumption_records
            where scenario = @scenario and source = @source;
            """;
        command.Parameters.AddWithValue("scenario", scenario);
        command.Parameters.AddWithValue("source", source);

        return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private async Task DeleteScenarioRecordsAsync(string scenario)
    {
        await using var connection = _postgres.CreateConnection();
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            delete from {PostgreSqlTestFixture.SchemaName}.event_consumption_records
            where scenario = @scenario;
            """;
        command.Parameters.AddWithValue("scenario", scenario);

        await command.ExecuteNonQueryAsync();
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, Func<string>? getFailureDetails = null)
    {
        var timeoutAt = DateTimeOffset.UtcNow.AddSeconds(10);

        while (DateTimeOffset.UtcNow < timeoutAt)
        {
            if (await condition())
                return;

            await Task.Delay(100);
        }

        var details = getFailureDetails is null ? string.Empty : $" {getFailureDetails()}";
        Assert.Fail($"等待 RabbitMQ 集成测试条件达成超时。{details}");
    }

    private static string NewScenarioName()
        => $"rabbitmq-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{Environment.CurrentManagedThreadId}";

    private static async Task InsertRecordAsync(PostgreSqlTestFixture postgres, string scenario, string source, string message, int attempt)
    {
        await using var connection = postgres.CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            insert into {PostgreSqlTestFixture.SchemaName}.event_consumption_records (scenario, source, message, attempt)
            values (@scenario, @source, @message, @attempt);
            """;
        command.Parameters.AddWithValue("scenario", scenario);
        command.Parameters.AddWithValue("source", source);
        command.Parameters.AddWithValue("message", message);
        command.Parameters.AddWithValue("attempt", attempt);
        await command.ExecuteNonQueryAsync();
    }

    private sealed record LocalRecordEvent(string Scenario, string Message) : IEvent;

    [RabbitMqEvent(RabbitMqTestRoutes.MainExchange, RabbitMqTestRoutes.MainRoutingKey)]
    private sealed record RabbitRecordEvent(string Scenario, string Message) : IEvent;

    private sealed record RetryRecordEvent(string Scenario, string Message) : IEvent;

    private sealed record DeadLetterRecordEvent(string Scenario, string Message) : IEvent;

    private sealed class LocalRecordHandler(PostgreSqlTestFixture postgres) : IEventHandler<LocalRecordEvent>
    {
        public Task HandleAsync(LocalRecordEvent eventData, CancellationToken cancellationToken = default)
            => InsertRecordAsync(postgres, eventData.Scenario, "local", eventData.Message, 1);
    }

    private sealed class RabbitRecordHandler(PostgreSqlTestFixture postgres) : IEventHandler<RabbitRecordEvent>
    {
        public Task HandleAsync(RabbitRecordEvent eventData, CancellationToken cancellationToken = default)
            => InsertRecordAsync(postgres, eventData.Scenario, "rabbit", eventData.Message, 1);
    }

    private sealed class RetryThenSuccessHandler(PostgreSqlTestFixture postgres) : IEventHandler<RetryRecordEvent>
    {
        private static readonly ConcurrentDictionary<string, int> Attempts = new();

        public async Task HandleAsync(RetryRecordEvent eventData, CancellationToken cancellationToken = default)
        {
            var attempt = Attempts.AddOrUpdate(eventData.Scenario, 1, (_, current) => current + 1);
            await InsertRecordAsync(postgres, eventData.Scenario, "retry-attempt", eventData.Message, attempt);
            if (attempt == 1)
                throw new InvalidOperationException("第一次消费失败，用于验证 RabbitMQ 重试。");
            await InsertRecordAsync(postgres, eventData.Scenario, "retry-success", eventData.Message, attempt);
        }
    }

    private sealed class AlwaysFailingHandler(PostgreSqlTestFixture postgres) : IEventHandler<DeadLetterRecordEvent>
    {
        public async Task HandleAsync(DeadLetterRecordEvent eventData, CancellationToken cancellationToken = default)
        {
            await InsertRecordAsync(postgres, eventData.Scenario, "dead-letter-attempt", eventData.Message, 1);
            throw new InvalidOperationException("消费持续失败，用于验证 RabbitMQ 死信。");
        }
    }

    private sealed class RabbitMqConsumerRegistration(IChannel channel, string consumerTag) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                if (channel.IsOpen) await channel.BasicCancelAsync(consumerTag);
            }
            finally
            {
                await channel.DisposeAsync();
            }
        }
    }
}

public sealed class RabbitMqTestFixture : IAsyncLifetime
{
    public IConnection Connection { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Connection = await new ConnectionFactory { Uri = new Uri(RabbitMqTestSettings.ConnectionString) }.CreateConnectionAsync();
    }

    public async Task<RabbitMqTopology> CreateTopologyAsync(string? mainExchange = null, string? mainRoutingKey = null)
    {
        var channel = await Connection.CreateChannelAsync();
        var topology = new RabbitMqTopology(channel, mainExchange, mainRoutingKey);
        try
        {
            await topology.DeclareAsync();
            return topology;
        }
        catch
        {
            await channel.DisposeAsync();
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        if (Connection is not null) await Connection.DisposeAsync();
    }
}

public sealed class RabbitMqTopology : IAsyncDisposable
{
    private readonly IChannel _channel;
    public string MainExchange { get; }
    public string DeadLetterExchange { get; }
    public string MainQueue { get; }
    public string RetryQueue { get; }
    public string DeadLetterQueue { get; }
    public string MainRoutingKey { get; }
    public string DeadLetterRoutingKey { get; }

    public RabbitMqTopology(IChannel channel, string? mainExchange = null, string? mainRoutingKey = null)
    {
        _channel = channel;
        var suffix = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.{Environment.CurrentManagedThreadId}";
        MainExchange = mainExchange ?? $"raindrop.tests.main.{suffix}";
        DeadLetterExchange = $"raindrop.tests.dlx.{suffix}";
        MainQueue = $"raindrop.tests.main.{suffix}";
        RetryQueue = $"raindrop.tests.retry.{suffix}";
        DeadLetterQueue = $"raindrop.tests.dlq.{suffix}";
        MainRoutingKey = mainRoutingKey ?? $"raindrop.tests.main.{suffix}";
        DeadLetterRoutingKey = $"raindrop.tests.dead.{suffix}";
    }

    public async Task DeclareAsync()
    {
        await _channel.ExchangeDeclareAsync(MainExchange, ExchangeType.Direct, true, false);
        await _channel.ExchangeDeclareAsync(DeadLetterExchange, ExchangeType.Direct, true, false);
        await _channel.QueueDeclareAsync(MainQueue, true, false, false, new Dictionary<string, object?>
        {
            ["x-dead-letter-exchange"] = DeadLetterExchange,
            ["x-dead-letter-routing-key"] = DeadLetterRoutingKey
        });
        await _channel.QueueDeclareAsync(RetryQueue, true, false, false, new Dictionary<string, object?>
        {
            ["x-dead-letter-exchange"] = MainExchange,
            ["x-dead-letter-routing-key"] = MainRoutingKey
        });
        await _channel.QueueDeclareAsync(DeadLetterQueue, true, false, false, null);
        await _channel.QueueBindAsync(MainQueue, MainExchange, MainRoutingKey);
        await _channel.QueueBindAsync(DeadLetterQueue, DeadLetterExchange, DeadLetterRoutingKey);
    }

    public async Task PublishAsync<TEvent>(TEvent eventData) where TEvent : IEvent
    {
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(eventData));
        var properties = new BasicProperties();
        properties.Persistent = true;
        properties.ContentType = "application/json";
        properties.Headers = new Dictionary<string, object?>();
        properties.Type = typeof(TEvent).AssemblyQualifiedName;
        await _channel.BasicPublishAsync(MainExchange, MainRoutingKey, false, properties, body);
    }

    public Task<uint> GetMessageCountAsync(string queue) => _channel.MessageCountAsync(queue);

    public async Task VerifyRetryRouteAsync()
    {
        var properties = new BasicProperties();
        properties.Expiration = "50";
        var body = Encoding.UTF8.GetBytes("probe");

        await _channel.BasicPublishAsync(string.Empty, RetryQueue, false, properties, body);

        var timeoutAt = DateTimeOffset.UtcNow.AddSeconds(5);
        BasicGetResult? result = null;
        while (DateTimeOffset.UtcNow < timeoutAt)
        {
            result = await _channel.BasicGetAsync(MainQueue, autoAck: false);
            if (result is not null)
            {
                await _channel.BasicAckAsync(result.DeliveryTag, false);
                return;
            }

            await Task.Delay(50);
        }

        Assert.Fail("RabbitMQ 重试拓扑没有把消息从 Retry Queue 路由回 Main Queue。");
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_channel.IsOpen)
            {
                await _channel.QueueDeleteAsync(DeadLetterQueue, false, false);
                await _channel.QueueDeleteAsync(RetryQueue, false, false);
                await _channel.QueueDeleteAsync(MainQueue, false, false);
                await _channel.ExchangeDeleteAsync(DeadLetterExchange, false);
                await _channel.ExchangeDeleteAsync(MainExchange, false);
            }
        }
        finally
        {
            await _channel.DisposeAsync();
        }
    }
}

public static class RabbitMqTestRoutes
{
    public const string MainExchange = "raindrop.tests.publish.main";
    public const string MainRoutingKey = "raindrop.tests.publish.main";
}
