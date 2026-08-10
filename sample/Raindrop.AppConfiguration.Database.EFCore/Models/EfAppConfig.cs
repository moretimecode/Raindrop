namespace Raindrop.AppConfiguration.Sample.Models;

public class EfAppConfig
{
    public string AppName { get; set; } = string.Empty;

    public int MaxItems { get; set; }

    public FeatureFlagsConfig FeatureFlags { get; set; } = new();
}

public class FeatureFlagsConfig
{
    public bool EnableNewUI { get; set; }
    public bool EnableLogging { get; set; } = true;
}
