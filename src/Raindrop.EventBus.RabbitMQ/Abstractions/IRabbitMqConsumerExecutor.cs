using Raindrop.Data.Events;
using Raindrop.EventBus.Abstractions;

namespace Raindrop.EventBus.RabbitMQ;

public interface IRabbitMqConsumerExecutor
{
    /// <summary>
    /// 执行单个 Handler，返回执行结果。ACK / Retry / DeadLetter 由 HostedService 统一控制。
    /// </summary>
    Task<RabbitMqHandlerExecutionResult> ExecuteAsync<TEvent, THandler>(
        TEvent eventData,
        CancellationToken cancellationToken = default)
        where TEvent : IEvent
        where THandler : IEventHandler<TEvent>;
}
