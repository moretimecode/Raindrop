using System.Reflection;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Raindrop.CQRS.Behaviors;
using Raindrop.CQRS.Builder;
using Raindrop.CQRS.Bus;
using Raindrop.CQRS.Context;

namespace Raindrop.CQRS;

/// <summary>
/// Raindrop CQRS 服务注册扩展。
/// </summary>
public static class CQRSServiceCollectionExtensions
{
    /// <summary>
    /// 注册 Raindrop CQRS 核心服务。
    /// </summary>
    /// <param name="services">IServiceCollection</param>
    /// <param name="applicationAssemblies">包含 Handler 的程序集列表</param>
    /// <returns>构建器，用于进一步注册 Pipeline Behavior</returns>
    public static IRaindropCQRSBuilder AddRaindropCQRS(
        this IServiceCollection services,
        Assembly[] applicationAssemblies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(applicationAssemblies);

        // 注册 MediatR
        services.AddMediatR(options =>
        {
            options.RegisterServicesFromAssemblies(applicationAssemblies);
        });

        // 注册 Bus
        services.TryAddScoped<ICommandBus, CommandBus>();
        services.TryAddScoped<IQueryBus, QueryBus>();

        // 注册 PipelineContext
        services.TryAddScoped<IPipelineContextAccessor, PipelineContextAccessor>();

        // 构建器
        var builder = new RaindropCQRSBuilder(services, applicationAssemblies);

        // 默认注册诊断行为
        builder.AddGenericPipeline(typeof(DiagnosticBehavior<,>));

        return builder;
    }
}
