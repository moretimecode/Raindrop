using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Raindrop.Data.Events;
using Raindrop.EventBus.Abstractions;
using Raindrop.EventBus.Core;

namespace Raindrop.EventBus.RabbitMQ;

/// <summary>
/// 基于 Handled Event 的 RabbitMQ 特性自动创建拓扑并启动消费者。
/// 负责消息确认/重试/死信的编排，Handler 执行委托给 RabbitMqConsumerExecutor。
/// </summary>
public sealed class RabbitMqHostedService : BackgroundService
{
    private readonly Assembly[] _assemblies;
    private readonly IRabbitMqConnection _connection;
    private readonly IRabbitMqTopologyDeclarer _declarer;
    private readonly IRabbitMqConsumerExecutor _executor;
    private readonly IRabbitMqDeadLetterRetryPolicyResolver _policyResolver;
    private readonly ConcurrentBag<ConsumerHandle> _consumers = [];

    public RabbitMqHostedService(
        Assembly[] assemblies,
        IRabbitMqConnection connection,
        IRabbitMqTopologyDeclarer declarer,
        IRabbitMqConsumerExecutor executor,
        IRabbitMqDeadLetterRetryPolicyResolver policyResolver)
    {
        _assemblies = assemblies ?? throw new ArgumentNullException(nameof(assemblies));
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _declarer = declarer ?? throw new ArgumentNullException(nameof(declarer));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _policyResolver = policyResolver ?? throw new ArgumentNullException(nameof(policyResolver));
    }

    /// <summary>
    /// 测试辅助：直接执行扫描与拓扑声明逻辑。
    /// </summary>
    public Task ExecuteTaskAsync(CancellationToken cancellationToken)
    {
        return ExecuteAsync(cancellationToken);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var registrations = EventHandlerRegistrationScanner.Scan(_assemblies);

        var handlerMap = registrations
            .GroupBy(r => r.EventType)
            .ToDictionary(g => g.Key, g => g.Select(r => r.ImplementationType).ToList());

        var groups = registrations
            .Select(r => r.EventType)
            .Distinct()
            .SelectMany(eventType => eventType
                .GetCustomAttributes<RabbitMqEventAttribute>(inherit: false)
                .Select(attribute => new
                {
                    eventType,
                    attribute.Exchange,
                    attribute.RoutingKey,
                    attribute.Queue
                }))
            .GroupBy(x => (x.Exchange, x.RoutingKey, QueueName: x.Queue ?? $"raindrop:{x.Exchange}:{x.RoutingKey}"))
            .ToArray();

        foreach (var group in groups)
        {
            var channel = _connection.CreateChannel();
            _declarer.Declare(channel, group.Key.Exchange, group.Key.RoutingKey, group.Key.QueueName);

            var eventTypesInGroup = group.Select(x => x.eventType).ToArray();
            var queueName = group.Key.QueueName;

            channel.BasicQos(0, 1, global: false);

            if (channel is RabbitMqChannelAdapter adapter)
            {
                var innerChannel = adapter.InnerChannel;
                var consumer = new AsyncEventingBasicConsumer(innerChannel);
                consumer.Received += async (_, args) =>
                {
                    await DispatchAsync(
                        innerChannel,
                        args,
                        eventTypesInGroup,
                        handlerMap,
                        queueName,
                        stoppingToken);
                };

                var consumerTag = channel.BasicConsume(queueName, autoAck: false, consumer);
                _consumers.Add(new ConsumerHandle(channel, consumerTag));
            }
            else
            {
                _consumers.Add(new ConsumerHandle(channel, null));
            }
        }

        return Task.CompletedTask;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        while (_consumers.TryTake(out var handle))
        {
            try
            {
                if (handle.ConsumerTag is not null)
                {
                    handle.Channel.BasicCancel(handle.ConsumerTag);
                }

                handle.Channel.Dispose();
            }
            catch
            {
            }
        }

        await base.StopAsync(cancellationToken);
    }

    private async Task DispatchAsync(
        IModel channel,
        BasicDeliverEventArgs args,
        Type[] eventTypesInGroup,
        Dictionary<Type, List<Type>> handlerMap,
        string queueName,
        CancellationToken cancellationToken)
    {
        var typeName = args.BasicProperties?.Type;
        if (string.IsNullOrWhiteSpace(typeName))
        {
            channel.BasicReject(args.DeliveryTag, false);
            return;
        }

        var eventType = eventTypesInGroup.FirstOrDefault(t => t.AssemblyQualifiedName == typeName);
        if (eventType is null)
        {
            channel.BasicReject(args.DeliveryTag, false);
            return;
        }

        if (!handlerMap.TryGetValue(eventType, out var handlerTypes) || handlerTypes.Count == 0)
        {
            channel.BasicReject(args.DeliveryTag, false);
            return;
        }

        IEvent? eventData;
        try
        {
            eventData = JsonSerializer.Deserialize(
                Encoding.UTF8.GetString(args.Body),
                eventType) as IEvent;
        }
        catch
        {
            channel.BasicReject(args.DeliveryTag, false);
            return;
        }

        if (eventData is null)
        {
            channel.BasicReject(args.DeliveryTag, false);
            return;
        }

        if (args.BasicProperties is null)
        {
            channel.BasicReject(args.DeliveryTag, false);
            return;
        }

        var failedCount = RabbitMqReceivedMessage.ReadFailedCount(args.BasicProperties);

        var retryQueue = $"{queueName}:retry";
        IRabbitMqReceivedMessage message = new RabbitMqReceivedMessage(
            channel,
            args.DeliveryTag,
            (byte[])args.Body.Clone(),
            args.BasicProperties,
            failedCount,
            retryQueue);

        // 执行所有 Handler，收集结果
        var results = new List<RabbitMqHandlerExecutionResult>(handlerTypes.Count);
        foreach (var handlerType in handlerTypes)
        {
            var result = await DispatchToHandlerAsync(eventData, handlerType, cancellationToken);
            results.Add(result);
        }

        // 聚合结果
        var decision = EvaluateDispatchResults(results, failedCount, _policyResolver);

        switch (decision.Action)
        {
            case MessageDispatchAction.Ack:
                await message.AckAsync(cancellationToken);
                break;
            case MessageDispatchAction.Retry:
                await message.RetryAsync(decision.RetryDelay, cancellationToken);
                break;
            case MessageDispatchAction.DeadLetter:
                await message.DeadLetterAsync(cancellationToken);
                break;
        }
    }

    /// <summary>
    /// 聚合所有 Handler 执行结果，产出统一的分发决策。
    /// </summary>
    internal static MessageDispatchDecision EvaluateDispatchResults(
        IReadOnlyList<RabbitMqHandlerExecutionResult> results,
        int failedCount,
        IRabbitMqDeadLetterRetryPolicyResolver policyResolver)
    {
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(policyResolver);

        bool hasRetry = false, hasDeadLetter = false;

        foreach (var result in results)
        {
            switch (result.Status)
            {
                case RabbitMqHandlerExecutionStatus.Succeeded:
                case RabbitMqHandlerExecutionStatus.AlreadyProcessed:
                    break;
                case RabbitMqHandlerExecutionStatus.Retry:
                    hasRetry = true;
                    break;
                case RabbitMqHandlerExecutionStatus.DeadLetter:
                    hasDeadLetter = true;
                    break;
            }
        }

        if (hasDeadLetter)
        {
            return new MessageDispatchDecision(MessageDispatchAction.DeadLetter, TimeSpan.Zero);
        }

        if (hasRetry)
        {
            var failedHandlerTypes = results
                .Where(x => x.Status == RabbitMqHandlerExecutionStatus.Retry)
                .Select(x => x.HandlerType);

            TimeSpan? minRetryDelay = null;
            int? minMaxRetryCount = null;

            foreach (var failedHandlerType in failedHandlerTypes)
            {
                var policy = policyResolver.Resolve(failedHandlerType);
                var delay = policy.GetRetryDelay(failedCount + 1);

                if (minRetryDelay is null || delay < minRetryDelay)
                    minRetryDelay = delay;

                if (minMaxRetryCount is null || policy.MaxRetryCount < minMaxRetryCount)
                    minMaxRetryCount = policy.MaxRetryCount;
            }

            if (minMaxRetryCount.HasValue && failedCount + 1 >= minMaxRetryCount.Value)
            {
                return new MessageDispatchDecision(MessageDispatchAction.DeadLetter, TimeSpan.Zero);
            }

            return new MessageDispatchDecision(
                MessageDispatchAction.Retry,
                minRetryDelay ?? TimeSpan.FromSeconds(2));
        }

        return new MessageDispatchDecision(MessageDispatchAction.Ack, TimeSpan.Zero);
    }

    private async Task<RabbitMqHandlerExecutionResult> DispatchToHandlerAsync(
        IEvent eventData,
        Type handlerType,
        CancellationToken cancellationToken)
    {
        try
        {
            var eventType = eventData.GetType();

            var method = typeof(IRabbitMqConsumerExecutor)
                .GetMethod(nameof(IRabbitMqConsumerExecutor.ExecuteAsync))!
                .MakeGenericMethod(eventType, handlerType);

            var task = (Task<RabbitMqHandlerExecutionResult>)method.Invoke(_executor, [eventData, cancellationToken])!;
            return await task;
        }
        catch (Exception ex)
        {
            return new RabbitMqHandlerExecutionResult(
                RabbitMqHandlerExecutionStatus.Retry,
                handlerType,
                ex);
        }
    }

    private sealed record ConsumerHandle(IRabbitMqChannel Channel, string? ConsumerTag);
}
