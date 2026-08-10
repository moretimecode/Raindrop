using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Raindrop.EventBus.Abstractions;

namespace Raindrop.EventBus.Core;

public sealed record EventHandlerRegistration(
    Type ServiceType,
    Type ImplementationType,
    Type EventType);

public static class EventHandlerRegistrationExtensions
{
    public static IReadOnlyCollection<EventHandlerRegistration> AddRaindropEventHandlers(
        this IServiceCollection services,
        IEnumerable<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(assemblies);

        var registrations = EventHandlerRegistrationScanner.Scan(assemblies);

        foreach (var registration in registrations)
        {
            services.TryAddEnumerable(ServiceDescriptor.Transient(
                registration.ServiceType,
                registration.ImplementationType));

            services.TryAdd(ServiceDescriptor.Transient(
                registration.ImplementationType,
                registration.ImplementationType));
        }

        return registrations;
    }

    public static IReadOnlyCollection<EventHandlerRegistration> AddRaindropEventHandlers(
        this IServiceCollection services,
        params Assembly[] assemblies)
    {
        return services.AddRaindropEventHandlers((IEnumerable<Assembly>)assemblies);
    }
}

public static class EventHandlerRegistrationScanner
{
    public static IReadOnlyCollection<EventHandlerRegistration> Scan(IEnumerable<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        return assemblies
            .Distinct()
            .SelectMany(assembly => assembly.DefinedTypes)
            .Where(type => type is { IsAbstract: false, IsInterface: false })
            .SelectMany(type => type
                .GetInterfaces()
                .Where(IsEventHandlerInterface)
                .Select(serviceType => new EventHandlerRegistration(
                    serviceType,
                    type.AsType(),
                    serviceType.GetGenericArguments()[0])))
            .ToArray();
    }

    private static bool IsEventHandlerInterface(Type type)
    {
        return type.IsGenericType &&
               type.GetGenericTypeDefinition() == typeof(IEventHandler<>);
    }
}
