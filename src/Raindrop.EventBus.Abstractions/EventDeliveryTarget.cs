namespace Raindrop.EventBus.Abstractions;

/// <summary>可持久化的发送地址。Transport 与 Version 是稳定协议标识，不是 CLR 类型名。</summary>
public sealed record EventDeliveryTarget(string Transport, string Version, string TargetJson);
