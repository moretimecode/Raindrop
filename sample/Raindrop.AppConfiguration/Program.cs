using Raindrop.AppConfiguration.Sample.Scenarios;

var scenario = args.FirstOrDefault()?.ToLowerInvariant() ?? "json";

switch (scenario)
{
    case "json":
        await LocalJsonConfigurationScenario.RunAsync();
        break;

    case "validation":
        await DataAnnotationsValidationScenario.RunAsync();
        break;

    case "all":
        await LocalJsonConfigurationScenario.RunAsync();
        await DataAnnotationsValidationScenario.RunAsync();
        break;

    default:
        Console.WriteLine("可用参数：json、validation、all。");
        break;
}
