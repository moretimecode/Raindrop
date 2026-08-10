namespace Raindrop.EventBus.RabbitMQ;

/// <summary>
/// Handler 执行状态，由 HostedService 聚合后决定消息的最终 ACK / Retry / DeadLetter。
/// </summary>
public enum RabbitMqHandlerExecutionStatus
{
    /// <summary>Handler 执行成功，可以提交。</summary>
    Succeeded,

    /// <summary>幂等判断：消息已被处理过，无需重复执行。</summary>
    AlreadyProcessed,

    /// <summary>执行失败，建议重试。</summary>
    Retry,

    /// <summary>执行失败，建议直接进入死信。</summary>
    DeadLetter
}

/// <summary>
/// 单 Handler 执行结果。HostedService 聚合所有 Handler 的结果后，统一决定消息的确认动作。
/// </summary>
public sealed record RabbitMqHandlerExecutionResult(
    RabbitMqHandlerExecutionStatus Status,
    Type HandlerType,
    Exception? Exception = null);
