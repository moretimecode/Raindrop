using Microsoft.EntityFrameworkCore;

namespace Raindrop.Data.EFCore;

/// <summary>将 Outbox 纳入业务 DbContext 的模型和迁移，不创建独立数据库。</summary>
public static class OutboxModelBuilderExtensions
{
    public static ModelBuilder AddRaindropOutbox(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        var entity = modelBuilder.Entity<OutboxMessage>();
        entity.ToTable("raindrop_outbox");
        entity.HasKey(x => x.DeliveryId);
        entity.Property(x => x.DeliveryId).HasMaxLength(64).IsUnicode(false);
        entity.Property(x => x.MessageId).HasMaxLength(64).IsUnicode(false).IsRequired();
        entity.Property(x => x.ContractName).HasMaxLength(200).IsRequired();
        entity.Property(x => x.ContractVersion).HasMaxLength(64).IsRequired();
        entity.Property(x => x.LegacyTypeName).HasMaxLength(2000).IsRequired();
        entity.Property(x => x.Payload).HasField("_payload").UsePropertyAccessMode(PropertyAccessMode.Field).IsRequired();
        entity.Property(x => x.PayloadHash).HasMaxLength(64).IsUnicode(false).IsRequired();
        entity.Property(x => x.Transport).HasMaxLength(100).IsUnicode(false).IsRequired();
        entity.Property(x => x.TargetVersion).HasMaxLength(32).IsUnicode(false).IsRequired();
        entity.Property(x => x.TargetJson).IsRequired();
        entity.Property(x => x.Status).HasMaxLength(32).IsUnicode(false).IsRequired();
        entity.HasIndex(x => x.MessageId);
        entity.HasIndex(x => x.CommitAttemptId);
        entity.HasIndex(x => new { x.Status, x.CreatedAtUtc });
        return modelBuilder;
    }
}
