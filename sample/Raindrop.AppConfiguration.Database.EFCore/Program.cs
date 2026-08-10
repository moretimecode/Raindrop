using Raindrop.AppConfiguration.Sample.Scenarios;

var scenario = args.FirstOrDefault()?.ToLowerInvariant() ?? "simple";

switch (scenario)
{
    case "simple":
        await EfCoreConfigurationScenario.RunAsync();
        break;

    case "fluent":
        await FluentEfCoreConfigurationScenario.RunAsync();
        break;

    case "mapster":
        await MapsterEfCoreConfigurationScenario.RunAsync();
        break;

    case "all":
        await EfCoreConfigurationScenario.RunAsync();
        await FluentEfCoreConfigurationScenario.RunAsync();
        await MapsterEfCoreConfigurationScenario.RunAsync();
        break;

    default:
        Console.WriteLine("可用参数：simple、fluent、mapster、all。");
        break;
}
