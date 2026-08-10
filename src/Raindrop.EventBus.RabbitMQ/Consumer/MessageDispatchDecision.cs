using Raindrop.EventBus.RabbitMQ;

namespace Raindrop.EventBus.RabbitMQ;

/// <summary>
/// 消息分发决策：HostedService 聚合所有 Handler 结果后的动作。
/// </summary>
internal enum MessageDispatchAction
{
    /// <summary>全部成功或已处理 → ACK。</summary>
    Ack,

    /// <summary>有 Handler 需要重试，且未达上限。</summary>
    Retry,

    /// <summary>有 Handler 需要死信，或已达重试上限。</summary>
    DeadLetter
}

/// <summary>
/// 消息分发决策结果。
/// </summary>
internal readonly record struct MessageDispatchDecision(
    MessageDispatchAction Action,
    TimeSpan RetryDelay);
