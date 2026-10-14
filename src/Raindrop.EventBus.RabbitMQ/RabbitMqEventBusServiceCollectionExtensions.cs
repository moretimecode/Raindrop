using System.Reflection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;
using Raindrop.EventBus.Abstractions;
using Raindrop.EventBus.Core;
using Raindrop.EventBus.RabbitMQ;

namespace Microsoft.Extensions.DependencyInjection;

public static class RabbitMqEventBusServiceCollectionExtensions
{
    public static IServiceCollection AddRabbitMqEventBus(
        this IServiceCollection services,
        params Assembly[] assemblies)
    {
        return services.AddRabbitMqEventBus(configure: null, assemblies);
    }

    public static IServiceCollection AddRabbitMqEventBus(
        this IServiceCollection services,
        Action<RabbitMqEventBusOptions>? configure,
        params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(assemblies);

        if (assemblies.Length == 0)
        {
            throw new ArgumentException("至少需要提供一个程序集用于扫描 RabbitMQ 事件处理器。", nameof(assemblies));
        }

        var options = new RabbitMqEventBusOptions();
        configure?.Invoke(options);

        services.AddRaindropEventHandlers(assemblies);
        services.AddEventBusCore<RabbitMqEventRouteResolver>();
        services.TryAddSingleton(options);
        services.TryAddSingleton<IRabbitMqConnection>(serviceProvider =>
        {
            var connection = serviceProvider.GetService<IConnection>();
            if (connection is not null)
                return new RabbitMqConnectionAdapter(connection);

            var rabbitMqOptions = serviceProvider.GetRequiredService<RabbitMqEventBusOptions>();
            var factory = new ConnectionFactory
            {
                Uri = new Uri(rabbitMqOptions.ConnectionString)
            };

            return new RabbitMqConnectionAdapter(factory);
        });

        services.TryAddEnumerable(ServiceDescriptor.Scoped<IEventTransport, RabbitMqEventTransport>());
        services.AddRabbitMqDeadLetterRetry();

        // HostedService 适配器
        services.TryAddSingleton<IRabbitMqTopologyDeclarer, RabbitMqTopologyDeclarer>();
        services.TryAddSingleton(sp => new RabbitMqHostedService(
            assemblies,
            sp.GetRequiredService<IRabbitMqConnection>(),
            sp.GetRequiredService<IRabbitMqTopologyDeclarer>(),
            sp.GetRequiredService<IRabbitMqConsumerExecutor>(),
            sp.GetRequiredService<IRabbitMqDeadLetterRetryPolicyResolver>()));
        // 通过 TryAddEnumerable 注册，ImplementationType = RabbitMqHostedService，避免工厂委托冲突
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, RabbitMqHostedService>(
                sp => sp.GetRequiredService<RabbitMqHostedService>()));

        return services;
    }

    public static IServiceCollection AddRabbitMqDeadLetterRetry(
        this IServiceCollection services,
        Action<RabbitMqDeadLetterRetryOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new RabbitMqDeadLetterRetryOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(options);
        services.TryAddSingleton<IConsumerIdempotencyKeyResolver, PropertyPathConsumerIdempotencyKeyResolver>();
        services.TryAddSingleton<IRabbitMqDeadLetterRetryPolicyResolver, RabbitMqDeadLetterRetryPolicyResolver>();
        services.TryAddSingleton<IRabbitMqConsumerExecutor, RabbitMqConsumerExecutor>();

        return services;
    }
}
