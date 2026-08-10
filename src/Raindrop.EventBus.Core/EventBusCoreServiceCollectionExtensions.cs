using Microsoft.Extensions.DependencyInjection.Extensions;
using Raindrop.EventBus.Abstractions;
using Raindrop.EventBus.Core;

namespace Microsoft.Extensions.DependencyInjection;

public static class EventBusCoreServiceCollectionExtensions
{
    public static IServiceCollection AddEventBusCore<TRouteResolver>(this IServiceCollection services)
        where TRouteResolver : class, IEventRouteResolver
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEventRouteResolver, TRouteResolver>());
        services.TryAddScoped<IEventBus, RoutingEventBus>();

        return services;
    }
}
