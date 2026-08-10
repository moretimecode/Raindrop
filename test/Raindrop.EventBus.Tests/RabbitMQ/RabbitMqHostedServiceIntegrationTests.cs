using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;
using Raindrop.Data.Events;
using Raindrop.EventBus.Abstractions;
using Raindrop.EventBus.RabbitMQ;

namespace Raindrop.AppConfiguration.Tests;

[Collection(PostgreSqlCollection.Name)]
public class RabbitMqHostedServiceIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlTestFixture _postgres;

    public RabbitMqHostedServiceIntegrationTests(PostgreSqlTestFixture postgres)
    {
        _postgres = postgres;
    }

    public Task InitializeAsync() => _postgres.ResetEventConsumptionRecordsAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    // 独立路由，避免与其他测试冲突
    private const string HostedExchange = "raindrop.tests.hosted.main";
    private const string HostedRoutingKey = "raindrop.tests.hosted.main";

    [Fact]
    public async Task HostedService_ConsumesRabbitMqEventAndInvokesHandler()
    {
        var scenario = NewScenarioName();
        await DeleteScenarioRecordsAsync(scenario);

        using var connection = CreateConnection();
        CleanupTopology(connection);

        using var host = BuildHost(connection);

        await host.StartAsync();
        await Task.Delay(1000);

        using var channel = connection.CreateModel();
        Publish(channel, new HostedServiceTestEvent(scenario, "hello"));

        await WaitUntilAsync(async () =>
            await CountRecordsAsync(scenario, "hosted-service") == 1);

        CleanupTopology(connection);
    }

    [Fact]
    public async Task HostedService_WhenHandlerFails_RetriesAndEventuallyMovesToDeadLetter()
    {
        var scenario = NewScenarioName();
        await DeleteScenarioRecordsAsync(scenario);

        using var connection = CreateConnection();
        CleanupTopology(connection);

        using var host = BuildHost(connection);

        await host.StartAsync();
        await Task.Delay(1000);

        using var channel = connection.CreateModel();
        Publish(channel, new HostedServiceFailingEvent(scenario, "fail"));

        await WaitUntilAsync(async () =>
            await CountRecordsAsync(scenario, "failing-attempt") >= 2);

        CleanupTopology(connection);
    }

    private static void CleanupTopology(IConnection connection)
    {
        try
        {
            using var channel = connection.CreateModel();
            var queue = $"raindrop:{HostedExchange}:{HostedRoutingKey}";
            channel.QueueDelete(queue, ifUnused: false, ifEmpty: false);
            channel.QueueDelete($"{queue}:retry", ifUnused: false, ifEmpty: false);
            channel.ExchangeDelete(HostedExchange, ifUnused: false);
        }
        catch
        {
            // 首次运行时拓扑可能不存在
        }
    }

    private IHost BuildHost(IConnection connection)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                // 清除默认 IHostedService 注册，只保留 RabbitMqHostedService
                services.AddSingleton(_postgres);
                services.AddSingleton(connection);
                services.AddRabbitMqDeadLetterRetry(options =>
                {
                    options.MaxRetryCount = 2;
                    options.InitialDelay = TimeSpan.FromMilliseconds(500);
                    options.MaxDelay = TimeSpan.FromMilliseconds(500);
                });
                services.AddRabbitMqEventBus(options =>
                {
                    options.ConnectionString = "amqp://admin:admin123@localhost:5672";
                }, typeof(RabbitMqHostedServiceIntegrationTests).Assembly);
            })
            .Build();
    }

    private static IConnection CreateConnection()
    {
        return new ConnectionFactory
        {
            Uri = new Uri("amqp://admin:admin123@localhost:5672"),
            DispatchConsumersAsync = true
        }.CreateConnection();
    }

    private static void Publish<TEvent>(IModel channel, TEvent eventData) where TEvent : IEvent
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(eventData);
        var properties = channel.CreateBasicProperties();
        properties.Persistent = true;
        properties.ContentType = "application/json";
        properties.Type = typeof(TEvent).AssemblyQualifiedName;
        properties.Headers = new Dictionary<string, object>();

        channel.BasicPublish(
            HostedExchange,
            HostedRoutingKey,
            mandatory: false,
            basicProperties: properties,
            body: body);
    }

    private async Task<int> CountRecordsAsync(string scenario, string source)
    {
        await using var conn = _postgres.CreateConnection();
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            select count(*)
            from {PostgreSqlTestFixture.SchemaName}.event_consumption_records
            where scenario = @scenario and source = @source;
            """;
        cmd.Parameters.AddWithValue("scenario", scenario);
        cmd.Parameters.AddWithValue("source", source);

        return Convert.ToInt32(await cmd.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private async Task DeleteScenarioRecordsAsync(string scenario)
    {
        await using var conn = _postgres.CreateConnection();
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            delete from {PostgreSqlTestFixture.SchemaName}.event_consumption_records
            where scenario = @scenario;
            """;
        cmd.Parameters.AddWithValue("scenario", scenario);

        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var timeoutAt = DateTimeOffset.UtcNow.AddSeconds(20);

        while (DateTimeOffset.UtcNow < timeoutAt)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(200);
        }

        Assert.Fail("等待 RabbitMQ 集成测试条件达成超时。");
    }

    private static string NewScenarioName()
    {
        return $"hosted-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{Environment.CurrentManagedThreadId}";
    }

    // ===== 测试用的事件和 Handler =====

    [RabbitMqEvent(HostedExchange, HostedRoutingKey)]
    internal sealed record HostedServiceTestEvent(string Scenario, string Message) : IEvent;

    [RabbitMqEvent(HostedExchange, HostedRoutingKey)]
    internal sealed record HostedServiceFailingEvent(string Scenario, string Message) : IEvent;

    internal sealed class HostedServiceTestHandler(PostgreSqlTestFixture postgres)
        : IEventHandler<HostedServiceTestEvent>
    {
        public Task HandleAsync(HostedServiceTestEvent eventData, CancellationToken cancellationToken = default)
        {
            return InsertRecordAsync(postgres, eventData.Scenario, "hosted-service", eventData.Message, 1);
        }
    }

    internal sealed class HostedServiceFailingHandler(PostgreSqlTestFixture postgres)
        : IEventHandler<HostedServiceFailingEvent>
    {
        private static readonly ConcurrentDictionary<string, int> Attempts = new();

        public async Task HandleAsync(HostedServiceFailingEvent eventData, CancellationToken cancellationToken = default)
        {
            var attempt = Attempts.AddOrUpdate(eventData.Scenario, 1, (_, current) => current + 1);
            await InsertRecordAsync(postgres, eventData.Scenario, "failing-attempt", eventData.Message, attempt);

            throw new InvalidOperationException("消费持续失败，用于验证 HostedService 重试与死信。");
        }
    }

    private static async Task InsertRecordAsync(
        PostgreSqlTestFixture postgres,
        string scenario,
        string source,
        string message,
        int attempt)
    {
        await using var conn = postgres.CreateConnection();
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            insert into {PostgreSqlTestFixture.SchemaName}.event_consumption_records
                (scenario, source, message, attempt)
            values
                (@scenario, @source, @message, @attempt);
            """;
        cmd.Parameters.AddWithValue("scenario", scenario);
        cmd.Parameters.AddWithValue("source", source);
        cmd.Parameters.AddWithValue("message", message);
        cmd.Parameters.AddWithValue("attempt", attempt);

        await cmd.ExecuteNonQueryAsync();
    }
}
