using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Raindrop.AspNetCore.Tests;

/// <summary>
/// OpenTelemetry 注册扩展测试。
/// </summary>
public class OpenTelemetryRegistrationTests
{
    [Fact]
    public void AddRaindropOpenTelemetry_WithoutConfiguration_DoesNotRegisterSdk()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        services.AddRaindropOpenTelemetry(configuration);

        Assert.DoesNotContain(services, IsOpenTelemetryService);
    }

    [Fact]
    public void AddRaindropOpenTelemetry_WithoutServiceName_DoesNotRegisterSdk()
    {
        var services = new ServiceCollection();
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Raindrop:OpenTelemetry:OtlpEndpoint"] = "http://localhost:4317"
        });

        services.AddRaindropOpenTelemetry(configuration);

        Assert.DoesNotContain(services, IsOpenTelemetryService);
    }

    [Fact]
    public void AddRaindropOpenTelemetry_WithoutOtlpEndpoint_DoesNotRegisterSdk()
    {
        var services = new ServiceCollection();
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Raindrop:OpenTelemetry:ServiceName"] = "order-api"
        });

        services.AddRaindropOpenTelemetry(configuration);

        Assert.DoesNotContain(services, IsOpenTelemetryService);
    }

    [Fact]
    public void AddRaindropOpenTelemetry_WithValidConfiguration_RegistersSdkHostedService()
    {
        var services = new ServiceCollection();
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Raindrop:OpenTelemetry:ServiceName"] = "order-api",
            ["Raindrop:OpenTelemetry:ServiceNamespace"] = "raindrop",
            ["Raindrop:OpenTelemetry:ServiceVersion"] = "1.0.0",
            ["Raindrop:OpenTelemetry:AutoGenerateServiceInstanceId"] = "true",
            ["Raindrop:OpenTelemetry:OtlpEndpoint"] = "http://localhost:4317"
        });

        services.AddRaindropOpenTelemetry(configuration);

        using var provider = services.BuildServiceProvider();
        var hostedServices = provider.GetServices<IHostedService>();

        Assert.Contains(hostedServices, service =>
            service.GetType().FullName?.Contains("OpenTelemetry", StringComparison.OrdinalIgnoreCase) == true);
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    private static bool IsOpenTelemetryService(ServiceDescriptor descriptor)
    {
        return descriptor.ServiceType.FullName?.StartsWith("OpenTelemetry.", StringComparison.Ordinal) == true
               || descriptor.ImplementationType?.FullName?.StartsWith("OpenTelemetry.", StringComparison.Ordinal) == true;
    }
}
