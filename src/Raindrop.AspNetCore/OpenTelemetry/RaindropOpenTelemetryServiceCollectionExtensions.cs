using Microsoft.Extensions.Configuration;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Raindrop.AspNetCore.OpenTelemetry;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Raindrop OpenTelemetry 服务注册扩展。
/// </summary>
public static class RaindropOpenTelemetryServiceCollectionExtensions
{
    private const string DefaultEnvironmentName = "Production";
    private const string AspNetCoreEnvironmentVariable = "ASPNETCORE_ENVIRONMENT";
    private const string DotNetEnvironmentVariable = "DOTNET_ENVIRONMENT";

    private static readonly string[] ActivitySourceNames =
    [
        "Raindrop.CQRS",
        "Raindrop.EventBus"
    ];

    /// <summary>
    /// 根据配置注册 Raindrop OpenTelemetry。配置缺失时不注册 SDK。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">应用配置。</param>
    public static IServiceCollection AddRaindropOpenTelemetry(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new RaindropOpenTelemetryOptions();
        configuration.GetSection(RaindropOpenTelemetryOptions.SectionName).Bind(options);

        if (!options.HasRequiredConfiguration())
        {
            return services;
        }

        var environmentName = ResolveEnvironmentName();
        var serviceInstanceId = options.ResolveServiceInstanceId();
        var otlpEndpoint = options.GetOtlpEndpoint();

        services
            .AddOpenTelemetry()
            .ConfigureResource(resource =>
            {
                resource.AddService(
                    serviceName: options.ServiceName!,
                    serviceNamespace: Normalize(options.ServiceNamespace),
                    serviceVersion: Normalize(options.ServiceVersion),
                    autoGenerateServiceInstanceId: false,
                    serviceInstanceId: serviceInstanceId);

                resource.AddAttributes(new Dictionary<string, object>
                {
                    ["deployment.environment.name"] = environmentName
                });
            })
            .WithTracing(tracing =>
            {
                tracing.AddAspNetCoreInstrumentation();

                foreach (var sourceName in ActivitySourceNames)
                {
                    tracing.AddSource(sourceName);
                }

                tracing.AddOtlpExporter(exporter =>
                {
                    exporter.Endpoint = otlpEndpoint;
                });
            });

        return services;
    }

    private static string ResolveEnvironmentName()
    {
        var aspNetCoreEnvironment = Environment.GetEnvironmentVariable(AspNetCoreEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(aspNetCoreEnvironment))
        {
            return aspNetCoreEnvironment;
        }

        var dotNetEnvironment = Environment.GetEnvironmentVariable(DotNetEnvironmentVariable);
        return string.IsNullOrWhiteSpace(dotNetEnvironment) ? DefaultEnvironmentName : dotNetEnvironment;
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
