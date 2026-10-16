using Raindrop.EventBus.Abstractions;

namespace Raindrop.Data.EFCore;

/// <summary>当前业务事务中的发送记录。此阶段仅登记 Pending，不代表已发送。</summary>
public sealed class OutboxMessage
{
    private byte[] _payload = [];
    private OutboxMessage() { }

    public string DeliveryId { get; private set; } = "";
    public string MessageId { get; private set; } = "";
    public Guid CommitAttemptId { get; private set; }
    public string ContractName { get; private set; } = "";
    public string ContractVersion { get; private set; } = "";
    public string LegacyTypeName { get; private set; } = "";
    public byte[] Payload { get => [.. _payload]; private set => _payload = value; }
    public string PayloadHash { get; private set; } = "";
    public string Transport { get; private set; } = "";
    public string TargetVersion { get; private set; } = "";
    public string TargetJson { get; private set; } = "";
    public DateTime CreatedAtUtc { get; private set; }
    public string Status { get; private set; } = "Pending";

    internal static OutboxMessage Create(string messageId, Guid attemptId, string contractName, string contractVersion,
        string legacyTypeName, byte[] payload, string hash, EventDeliveryTarget target, DateTime createdAtUtc)
        => new()
        {
            DeliveryId = Guid.NewGuid().ToString("N"),
            MessageId = messageId,
            CommitAttemptId = attemptId,
            ContractName = contractName,
            ContractVersion = contractVersion,
            LegacyTypeName = legacyTypeName,
            Payload = [.. payload],
            PayloadHash = hash,
            Transport = target.Transport,
            TargetVersion = target.Version,
            TargetJson = target.TargetJson,
            CreatedAtUtc = createdAtUtc
        };
}
