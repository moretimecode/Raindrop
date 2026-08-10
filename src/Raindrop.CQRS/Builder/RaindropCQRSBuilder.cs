using System.Reflection;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Raindrop.CQRS.Builder;

/// <summary>
/// CQRS 构建器默认实现。
/// </summary>
public class RaindropCQRSBuilder : IRaindropCQRSBuilder
{
    /// <inheritdoc />
    public IServiceCollection Services { get; }

    /// <inheritdoc />
    public Assembly[] Assemblies { get; }

    /// <summary>
    /// 初始化构建器。
    /// </summary>
    /// <exception cref="ArgumentNullException">assemblies 为空时抛出</exception>
    public RaindropCQRSBuilder(IServiceCollection services, Assembly[] assemblies)
    {
        if (assemblies is not { Length: > 0 })
            throw new ArgumentNullException(nameof(assemblies));

        Services = services;
        Assemblies = assemblies;
    }

    /// <inheritdoc />
    public IRaindropCQRSBuilder AddGenericPipeline(Type implementationType,
        ServiceLifetime serviceLifetime = ServiceLifetime.Transient)
    {
        if (implementationType.IsGenericType && implementationType.GetGenericArguments().Length == 2)
        {
            var descriptor = new ServiceDescriptor(
                typeof(IPipelineBehavior<,>), implementationType, serviceLifetime);
            Services.TryAddEnumerable(descriptor);
        }
        else
        {
            throw new ArgumentException(
                "implementationType must be an open generic type with two generic arguments");
        }

        return this;
    }

    /// <inheritdoc />
    public IRaindropCQRSBuilder AddConcretePipeline<TImplementationType, TRequest, TResponse>(
        ServiceLifetime serviceLifetime = ServiceLifetime.Transient)
        where TRequest : notnull
    {
        var descriptor = new ServiceDescriptor(
            typeof(IPipelineBehavior<TRequest, TResponse>),
            typeof(TImplementationType),
            serviceLifetime);
        Services.TryAddEnumerable(descriptor);

        return this;
    }
}
