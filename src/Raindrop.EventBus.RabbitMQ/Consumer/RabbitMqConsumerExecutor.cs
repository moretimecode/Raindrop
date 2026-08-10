using Microsoft.Extensions.DependencyInjection;
using Raindrop.Data.Events;
using Raindrop.EventBus.Abstractions;
using Raindrop.UnitOfWork.Abstractions;

namespace Raindrop.EventBus.RabbitMQ;

/// <summary>
/// 单 Handler 执行器。负责 Scope、UoW、幂等检查与状态写入，
/// 但 ACK / Retry / DeadLetter 由 HostedService 控制。
/// </summary>
public sealed class RabbitMqConsumerExecutor(
    IServiceScopeFactory serviceScopeFactory) : IRabbitMqConsumerExecutor
{
    private readonly IServiceScopeFactory _serviceScopeFactory = serviceScopeFactory;

    public async Task<RabbitMqHandlerExecutionResult> ExecuteAsync<TEvent, THandler>(
        TEvent eventData,
        CancellationToken cancellationToken = default)
        where TEvent : IEvent
        where THandler : IEventHandler<TEvent>
    {
        ArgumentNullException.ThrowIfNull(eventData);

        await using var scope = _serviceScopeFactory.CreateAsyncScope();

        try
        {
            var handler = scope.ServiceProvider.GetRequiredService<THandler>();
            var unitOfWorkFactory = scope.ServiceProvider.GetService<IUnitOfWorkFactory>();
            var requiresUnitOfWork = typeof(THandler)
                .GetCustomAttributes(typeof(UnitOfWorkAttribute), inherit: true)
                .Any();

            var idempotencyAttribute = typeof(THandler)
                .GetCustomAttributes(typeof(IdempotentConsumerAttribute), false)
                .OfType<IdempotentConsumerAttribute>()
                .SingleOrDefault();

            string? idempotencyKey = null;
            IConsumerIdempotencyStore? idempotencyStore = null;

            // 幂等检查
            if (idempotencyAttribute is not null)
            {
                idempotencyStore = scope.ServiceProvider.GetRequiredService<IConsumerIdempotencyStore>();
                var keyResolver = scope.ServiceProvider.GetRequiredService<IConsumerIdempotencyKeyResolver>();
                idempotencyKey = keyResolver.Resolve(eventData, typeof(THandler), idempotencyAttribute);

                if (idempotencyAttribute.ExpireSeconds <= 0)
                {
                    throw new InvalidOperationException(
                        $"{typeof(THandler).FullName} 的幂等过期时间必须大于 0 秒。");
                }

                var beginResult = await idempotencyStore.TryBeginAsync(
                    idempotencyKey,
                    TimeSpan.FromSeconds(idempotencyAttribute.ExpireSeconds),
                    cancellationToken);

                if (beginResult == ConsumerIdempotencyBeginResult.Succeeded)
                {
                    return new RabbitMqHandlerExecutionResult(
                        RabbitMqHandlerExecutionStatus.AlreadyProcessed,
                        typeof(THandler));
                }

                if (beginResult == ConsumerIdempotencyBeginResult.Processing)
                {
                    return idempotencyAttribute.ConflictBehavior switch
                    {
                        IdempotentConflictBehavior.Ack => new RabbitMqHandlerExecutionResult(
                            RabbitMqHandlerExecutionStatus.AlreadyProcessed, typeof(THandler)),
                        IdempotentConflictBehavior.Retry => new RabbitMqHandlerExecutionResult(
                            RabbitMqHandlerExecutionStatus.Retry, typeof(THandler)),
                        _ => throw new InvalidOperationException(
                            $"{typeof(THandler).FullName} 的幂等 Key 正在处理中。")
                    };
                }
            }

            // 执行 Handler
            IUnitOfWork? unitOfWork = null;
            try
            {
                unitOfWork = !requiresUnitOfWork || unitOfWorkFactory is null
                    ? null
                    : await unitOfWorkFactory.BeginAsync(cancellationToken);

                await handler.HandleAsync(eventData, cancellationToken);

                if (unitOfWork is not null)
                    await unitOfWork.CommitAsync(cancellationToken);

                if (idempotencyKey is not null && idempotencyStore is not null)
                    await idempotencyStore.MarkSucceededAsync(idempotencyKey, cancellationToken);

                return new RabbitMqHandlerExecutionResult(
                    RabbitMqHandlerExecutionStatus.Succeeded,
                    typeof(THandler));
            }
            catch (Exception ex)
            {
                if (unitOfWork is not null)
                    await TryRollbackAsync(unitOfWork, cancellationToken);

                if (idempotencyKey is not null && idempotencyStore is not null)
                    await TryMarkFailedAsync(idempotencyStore, idempotencyKey, cancellationToken);

                return new RabbitMqHandlerExecutionResult(
                    RabbitMqHandlerExecutionStatus.Retry,
                    typeof(THandler),
                    ex);
            }
            finally
            {
                if (unitOfWork is not null)
                    await unitOfWork.DisposeAsync();
            }
        }
        catch (Exception ex)
        {
            // Handler 构造失败等顶层异常 → Retry
            return new RabbitMqHandlerExecutionResult(
                RabbitMqHandlerExecutionStatus.Retry,
                typeof(THandler),
                ex);
        }
    }

    private static async Task TryRollbackAsync(IUnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        try
        {
            await unitOfWork.RollbackAsync(cancellationToken);
        }
        catch
        {
        }
    }

    private static async Task TryMarkFailedAsync(
        IConsumerIdempotencyStore idempotencyStore,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        try
        {
            await idempotencyStore.MarkFailedAsync(idempotencyKey, cancellationToken);
        }
        catch
        {
        }
    }
}
