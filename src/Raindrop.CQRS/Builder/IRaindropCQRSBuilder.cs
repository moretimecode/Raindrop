using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Raindrop.CQRS.Builder;

/// <summary>
/// CQRS 构建器接口。提供流式注册 Pipeline Behavior 和程序集的能力。
/// </summary>
public interface IRaindropCQRSBuilder
{
    /// <summary>
    /// 获取当前 IServiceCollection。
    /// </summary>
    IServiceCollection Services { get; }

    /// <summary>
    /// 获取注册的程序集列表。
    /// </summary>
    Assembly[] Assemblies { get; }

    /// <summary>
    /// 注册一个开放泛型 Pipeline Behavior（如 <c>typeof(DiagnosticBehavior&lt;,&gt;)</c>）。
    /// </summary>
    IRaindropCQRSBuilder AddGenericPipeline(Type implementationType,
        ServiceLifetime serviceLifetime = ServiceLifetime.Transient);

    /// <summary>
    /// 注册一个封闭泛型 Pipeline Behavior。
    /// </summary>
    IRaindropCQRSBuilder AddConcretePipeline<TImplementationType, TRequest, TResponse>(
        ServiceLifetime serviceLifetime = ServiceLifetime.Transient)
        where TRequest : notnull;
}
