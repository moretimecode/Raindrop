using System.Reflection;
using MediatR;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Raindrop.EventBus.Abstractions;
using Raindrop.EventBus.Core;
using Raindrop.EventBus.MediatR;

namespace Microsoft.Extensions.DependencyInjection;

public static class MediatRLocalEventBusServiceCollectionExtensions
{
    public static IServiceCollection AddMediatRLocalEventBus(
        this IServiceCollection services,
        params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(assemblies);

        if (assemblies.Length == 0)
        {
            throw new ArgumentException("至少需要提供一个程序集用于扫描本地事件处理器。", nameof(assemblies));
        }

        if (services.HasService<IMediator>())
        {
            services.EnsureCompleteMediatRCoreRegistration();
        }
        else
        {
            services.AddMediatR(configuration =>
            {
                configuration.Lifetime = ServiceLifetime.Scoped;
                configuration.RegisterServicesFromAssemblies(assemblies);
            });
        }

        services.AddEventBusCore<MediatREventRouteResolver>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IEventTransport, MediatRLocalEventTransport>());

        var registrations = services.AddRaindropEventHandlers(assemblies);
        RegisterMediatRNotificationHandlers(services, registrations);

        return services;
    }

    private static void RegisterMediatRNotificationHandlers(
        IServiceCollection services,
        IEnumerable<EventHandlerRegistration> registrations)
    {
        foreach (var registration in registrations)
        {
            var notificationHandlerType = typeof(INotificationHandler<>).MakeGenericType(
                typeof(MediatREventNotification<>).MakeGenericType(registration.EventType));

            var adapterType = typeof(MediatREventNotificationHandler<>)
                .MakeGenericType(registration.EventType);

            services.TryAddEnumerable(ServiceDescriptor.Transient(
                notificationHandlerType,
                adapterType));
        }
    }

    private static bool HasService<TService>(this IServiceCollection services)
    {
        return services.Any(descriptor => descriptor.ServiceType == typeof(TService));
    }

    private static void EnsureCompleteMediatRCoreRegistration(this IServiceCollection services)
    {
        if (services.HasService<ISender>() && services.HasService<IPublisher>())
        {
            return;
        }

        throw new InvalidOperationException(
            "检测到 IMediator 已注册，但 ISender 或 IPublisher 未注册。请先使用 MediatR 的 AddMediatR 完成核心注册，或移除手工注册后再调用 AddMediatRLocalEventBus。");
    }
}
