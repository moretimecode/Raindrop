using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Raindrop.EventBus.Abstractions;

namespace Raindrop.EventBus.Core;

public static class ConsumerIdempotencyServiceCollectionExtensions
{
    public static IServiceCollection AddInMemoryConsumerIdempotency(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IConsumerIdempotencyStore, InMemoryConsumerIdempotencyStore>();

        return services;
    }
}