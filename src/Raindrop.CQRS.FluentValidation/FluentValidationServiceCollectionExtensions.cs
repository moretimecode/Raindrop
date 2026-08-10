using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Raindrop.CQRS.Builder;
using Raindrop.CQRS.FluentValidation.Behaviors;

namespace Raindrop.CQRS.FluentValidation;

/// <summary>
/// CQRS FluentValidation 服务注册扩展。
/// </summary>
public static class FluentValidationServiceCollectionExtensions
{
    /// <summary>
    /// 注册 FluentValidation 验证管道。自动扫描指定程序集中的 <see cref="IValidator{T}"/> 实现。
    /// </summary>
    /// <param name="builder">CQRS 构建器</param>
    /// <param name="configureOptions">验证配置选项</param>
    /// <returns>构建器，用于进一步注册</returns>
    public static IRaindropCQRSBuilder AddValidation(
        this IRaindropCQRSBuilder builder,
        Action<CQRSValidationOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // 注册 ValidatorBehavior 作为全局 Pipeline
        builder.AddGenericPipeline(typeof(ValidatorBehavior<,>));

        var services = builder.Services;

        // 手动扫描注册所有 IValidator<> 实现
        RegisterValidators(services, builder.Assemblies);

        // 注册默认失败处理器
        services.TryAddSingleton<IValidationFailureHandler, DefaultValidationFailureHandler>();

        // 配置选项（默认值通过 CQRSValidationOptions 的零值表示，由 DefaultValidationFailureHandler 回退）
        var optionsBuilder = services.AddOptions<CQRSValidationOptions>();

        if (configureOptions is not null)
        {
            optionsBuilder.Configure(configureOptions);
        }

        return builder;
    }

    /// <summary>
    /// 手动扫描程序集，注册所有 IValidator&lt;&gt; 实现为 Scoped。
    /// </summary>
    private static void RegisterValidators(IServiceCollection services, Assembly[] assemblies)
    {
        var validatorType = typeof(IValidator<>);

        foreach (var assembly in assemblies)
        {
            foreach (var type in assembly.GetTypes())
            {
                if (type.IsAbstract || type.IsInterface)
                    continue;

                // 查找实现的 IValidator<> 接口
                var validatorInterfaces = type.GetInterfaces()
                    .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == validatorType)
                    .ToList();

                foreach (var iface in validatorInterfaces)
                {
                    services.TryAddScoped(iface, type);
                }
            }
        }
    }
}
