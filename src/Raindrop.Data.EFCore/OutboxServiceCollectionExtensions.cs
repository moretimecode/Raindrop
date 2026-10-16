using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Raindrop.Data.EFCore;
using Raindrop.EventBus.Abstractions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>显式接入与业务共用 DbContext 的 Outbox 登记；不启动后台派发。</summary>
public static class OutboxServiceCollectionExtensions
{
    public static IServiceCollection AddRaindropOutbox<TDbContext>(this IServiceCollection services, Action<OutboxOptions> configure)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        if (services.Any(x => x.ServiceType == typeof(IEventOutbox)))
            throw new InvalidOperationException("同一宿主只允许一个 Outbox 消息 DbContext，不能重复或覆盖登记器。");
        var options = new OutboxOptions();
        configure(options);
        services.AddSingleton(options.Freeze());
        services.TryAddScoped<UnitOfWorkSession<TDbContext>>();
        services.TryAddScoped<OutboxTransactionBinding>();
        services.AddScoped<IEventOutbox, EntityFrameworkCoreEventOutbox<TDbContext>>();
        return services;
    }
}
