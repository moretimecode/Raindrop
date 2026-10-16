using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Raindrop.Data.Events;
using Raindrop.EventBus.Abstractions;

namespace Raindrop.Data.EFCore;

/// <summary>预检全部发送目标及内容后，仅向当前业务 DbContext 添加记录，不保存、不发送。</summary>
internal sealed class EntityFrameworkCoreEventOutbox<TDbContext>(
    TDbContext context,
    UnitOfWorkSession<TDbContext> session,
    OutboxContractCatalog contracts,
    IEnumerable<IEventRouteResolver> routeResolvers,
    IEnumerable<IEventTransport> transports,
    OutboxTransactionBinding binding) : IEventOutbox where TDbContext : DbContext
{
    public Task EnqueueAsync(IIntegrationEvent eventData, CancellationToken cancellationToken = default)
    {
        // 未开始的会话拒绝调用，但不妨碍随后正常 Begin；活动会话的失败则必须终止提交。
        binding.EnsureContext(context);
        session.EnsureRegistrationAllowed();
        try
        {
            ArgumentNullException.ThrowIfNull(eventData);
            cancellationToken.ThrowIfCancellationRequested();
            if (session.IsRegistering)
                throw new InvalidOperationException("不支持并发或重入集成事件登记。");
            session.IsRegistering = true;
            EnsureSharedTransaction();
            var eventType = eventData.GetType();
            var contract = contracts.Resolve(eventType);
            var targets = CaptureTargets(eventType);
            var payload = JsonSerializer.SerializeToUtf8Bytes(eventData, eventType);
            var hash = Convert.ToHexString(SHA256.HashData(payload));
            var legacyTypeName = eventType.AssemblyQualifiedName
                ?? throw new InvalidOperationException("事件缺少兼容类型名称。");
            if (legacyTypeName.Length > 2000)
                throw new InvalidOperationException("事件兼容类型名称超过 2000 个字符。");
            cancellationToken.ThrowIfCancellationRequested();
            binding.EnsureContext(context);
            session.EnsureRegistrationAllowed();
            var messageId = Guid.NewGuid().ToString("N");
            var now = DateTime.UtcNow;
            var messages = targets.Select(target => OutboxMessage.Create(messageId, session.CommitAttemptId,
                contract.Name, contract.Version, legacyTypeName, payload, hash, target, now)).ToArray();

            context.AddRange(messages);
            session.MessageIds.Add(messageId);
            return Task.CompletedTask;
        }
        catch
        {
            session.State = UnitOfWorkState.Failed;
            throw;
        }
        finally
        {
            session.IsRegistering = false;
        }
    }

    private void EnsureSharedTransaction()
    {
        if (!context.Database.IsRelational())
            throw new InvalidOperationException("Outbox 同事务登记要求关系型 EF Core provider。");
        if (context.Model.FindEntityType(typeof(OutboxMessage)) is null)
            throw new InvalidOperationException("业务 DbContext 尚未调用 modelBuilder.AddRaindropOutbox()。");
        if (context.Database.CurrentTransaction is not { } transaction || transaction.TransactionId != session.TransactionId)
            throw new InvalidOperationException("Outbox 必须使用当前 Raindrop UoW 开启的原始数据库事务。");
    }

    private EventDeliveryTarget[] CaptureTargets(Type eventType)
    {
        var routes = routeResolvers.SelectMany(x => x.Resolve(eventType)).ToArray();
        if (routes.Length == 0 || routes.Any(x => x.Metadata is LocalEventAttribute))
            throw new InvalidOperationException($"集成事件 {eventType.Name} 必须具有明确的远程路由，不能使用默认本地路由或混合本地路由。");
        var availableTransports = transports.ToArray();
        var targets = routes.Select(route =>
        {
            var matches = availableTransports.Where(x => x.CanHandle(route)).ToArray();
            if (matches.Length != 1)
                throw new InvalidOperationException($"路由 {route.Metadata.GetType().Name} 必须且只能匹配一个 transport，实际为 {matches.Length} 个。");
            var target = matches[0].CaptureTarget(route);
            if (target is null || string.IsNullOrWhiteSpace(target.Transport) || target.Transport.Length > 100 ||
                string.IsNullOrWhiteSpace(target.Version) || target.Version.Length > 32 || string.IsNullOrWhiteSpace(target.TargetJson))
                throw new InvalidOperationException("发送目标缺少合法的 transport、目标版本或 JSON。");
            using var json = JsonDocument.Parse(target.TargetJson);
            if (json.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("发送目标 JSON 必须为对象。");
            return target;
        }).ToArray();
        if (targets.Distinct().Count() != targets.Length)
            throw new InvalidOperationException("同一发布包含重复发送目标。");
        return targets;
    }
}
