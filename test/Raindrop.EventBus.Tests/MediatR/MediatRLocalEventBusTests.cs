using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Raindrop.Data.Events;
using Raindrop.EventBus.Abstractions;
using Raindrop.EventBus.MediatR;
using Raindrop.EventBus.RabbitMQ;

namespace Raindrop.AppConfiguration.Tests;

public class MediatRLocalEventBusTests
{
    [Fact]
    public void AddMediatRLocalEventBus_RegistersCoreServicesAsScopedAndHandlersAsTransient()
    {
        using var provider = new ServiceCollection()
            .AddMediatRLocalEventBus(typeof(MediatRLocalEventBusTests).Assembly)
            .BuildServiceProvider();

        using var scope = provider.CreateScope();

        var firstEventBus = scope.ServiceProvider.GetRequiredService<IEventBus>();
        var secondEventBus = scope.ServiceProvider.GetRequiredService<IEventBus>();
        var routeResolver = scope.ServiceProvider.GetRequiredService<IEventRouteResolver>();
        var firstHandler = scope.ServiceProvider.GetRequiredService<IEventHandler<CounterEvent>>();
        var secondHandler = scope.ServiceProvider.GetRequiredService<IEventHandler<CounterEvent>>();

        Assert.Same(firstEventBus, secondEventBus);
        Assert.IsType<MediatREventRouteResolver>(routeResolver);
        Assert.NotSame(firstHandler, secondHandler);
    }

    [Fact]
    public async Task AddMediatRLocalEventBus_WhenMediatRAlreadyRegistered_DoesNotRegisterCoreAgain()
    {
        CounterEventHandler.HandledCount = 0;

        var services = new ServiceCollection();
        services.AddMediatR(configuration =>
        {
            configuration.Lifetime = ServiceLifetime.Transient;
            configuration.RegisterServicesFromAssemblies(typeof(MediatRLocalEventBusTests).Assembly);
        });

        services.AddMediatRLocalEventBus(typeof(MediatRLocalEventBusTests).Assembly);

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IMediator));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ISender));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IPublisher));

        using var provider = services.BuildServiceProvider();
        var eventBus = provider.GetRequiredService<IEventBus>();

        await eventBus.PublishAsync(new CounterEvent());

        Assert.Equal(1, CounterEventHandler.HandledCount);
    }

    [Fact]
    public void AddMediatRLocalEventBus_AfterRabbitMqEventBus_KeepsBothRouteResolversAndTransports()
    {
        var services = new ServiceCollection()
            .AddRabbitMqEventBus(typeof(MediatRLocalEventBusTests).Assembly)
            .AddMediatRLocalEventBus(typeof(MediatRLocalEventBusTests).Assembly);

        var routeResolvers = services
            .Where(descriptor => descriptor.ServiceType == typeof(IEventRouteResolver))
            .ToArray();

        Assert.Contains(routeResolvers, descriptor => descriptor.ImplementationType == typeof(MediatREventRouteResolver));
        Assert.Contains(routeResolvers, descriptor => descriptor.ImplementationType == typeof(RabbitMqEventRouteResolver));
        Assert.Contains(services, IsMediatRTransportRegistration);
        Assert.Contains(services, IsRabbitMqTransportRegistration);
    }

    [Fact]
    public void AddRabbitMqEventBus_AfterMediatRLocalEventBus_KeepsBothTransports()
    {
        var services = new ServiceCollection()
            .AddMediatRLocalEventBus(typeof(MediatRLocalEventBusTests).Assembly)
            .AddRabbitMqEventBus(typeof(MediatRLocalEventBusTests).Assembly);

        var routeResolvers = services
            .Where(descriptor => descriptor.ServiceType == typeof(IEventRouteResolver))
            .ToArray();

        Assert.Contains(routeResolvers, descriptor => descriptor.ImplementationType == typeof(MediatREventRouteResolver));
        Assert.Contains(routeResolvers, descriptor => descriptor.ImplementationType == typeof(RabbitMqEventRouteResolver));
        Assert.Contains(services, IsMediatRTransportRegistration);
        Assert.Contains(services, IsRabbitMqTransportRegistration);
    }

    [Fact]
    public void AddMediatRLocalEventBus_WhenMediatRCoreIsIncomplete_ThrowsClearException()
    {
        var services = new ServiceCollection();
        services.AddScoped<IMediator, Mediator>();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddMediatRLocalEventBus(typeof(MediatRLocalEventBusTests).Assembly));

        Assert.Contains("IPublisher", exception.Message);
    }

    [Fact]
    public async Task PublishAsync_EventWithoutRouteAttribute_DispatchesToLocalHandler()
    {
        CounterEventHandler.HandledCount = 0;

        using var provider = new ServiceCollection()
            .AddMediatRLocalEventBus(typeof(MediatRLocalEventBusTests).Assembly)
            .BuildServiceProvider();

        var eventBus = provider.GetRequiredService<IEventBus>();

        await eventBus.PublishAsync(new CounterEvent());

        Assert.Equal(1, CounterEventHandler.HandledCount);
    }

    [Fact]
    public async Task PublishAsync_EventWithLocalEventAttribute_DispatchesToLocalHandler()
    {
        LocalCounterEventHandler.HandledCount = 0;

        using var provider = new ServiceCollection()
            .AddMediatRLocalEventBus(typeof(MediatRLocalEventBusTests).Assembly)
            .BuildServiceProvider();

        var eventBus = provider.GetRequiredService<IEventBus>();

        await eventBus.PublishAsync(new LocalCounterEvent());

        Assert.Equal(1, LocalCounterEventHandler.HandledCount);
    }

    [Fact]
    public async Task PublishAsync_EventWithOnlyRemoteRouteAttribute_WhenNoTransportRegistered_ThrowsAndDoesNotDispatchToLocalHandler()
    {
        RemoteCounterEventHandler.HandledCount = 0;

        using var provider = new ServiceCollection()
            .AddMediatRLocalEventBus(typeof(MediatRLocalEventBusTests).Assembly)
            .BuildServiceProvider();

        var eventBus = provider.GetRequiredService<IEventBus>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            eventBus.PublishAsync(new RemoteCounterEvent()));

        Assert.Contains("没有解析到任何事件总线路由", exception.Message);
        Assert.Equal(0, RemoteCounterEventHandler.HandledCount);
    }

    private sealed record CounterEvent : IEvent;

    [LocalEvent]
    private sealed record LocalCounterEvent : IEvent;

    [RemoteEvent]
    private sealed record RemoteCounterEvent : IEvent;

    private sealed class CounterEventHandler : IEventHandler<CounterEvent>
    {
        public static int HandledCount { get; set; }

        public Task HandleAsync(CounterEvent eventData, CancellationToken cancellationToken = default)
        {
            HandledCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class LocalCounterEventHandler : IEventHandler<LocalCounterEvent>
    {
        public static int HandledCount { get; set; }

        public Task HandleAsync(LocalCounterEvent eventData, CancellationToken cancellationToken = default)
        {
            HandledCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class RemoteCounterEventHandler : IEventHandler<RemoteCounterEvent>
    {
        public static int HandledCount { get; set; }

        public Task HandleAsync(RemoteCounterEvent eventData, CancellationToken cancellationToken = default)
        {
            HandledCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class RemoteEventAttribute : EventRouteAttribute
    {
    }

    private static bool IsMediatRTransportRegistration(ServiceDescriptor descriptor)
    {
        return descriptor.ServiceType == typeof(IEventTransport) &&
               descriptor.ImplementationType == typeof(MediatRLocalEventTransport);
    }

    private static bool IsRabbitMqTransportRegistration(ServiceDescriptor descriptor)
    {
        return descriptor.ServiceType == typeof(IEventTransport) &&
               descriptor.ImplementationType == typeof(RabbitMqEventTransport);
    }
}
