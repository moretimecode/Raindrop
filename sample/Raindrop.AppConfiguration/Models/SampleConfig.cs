using System.ComponentModel.DataAnnotations;

namespace Raindrop.AppConfiguration.Sample.Models;

public class SampleConfig
{
    [Required(ErrorMessage = "AppName 不能为空")]
    [StringLength(50, MinimumLength = 2, ErrorMessage = "AppName 长度必须在 2 到 50 之间")]
    public string AppName { get; set; } = string.Empty;

    [Range(1, 10_000, ErrorMessage = "MaxItems 必须在 1 到 10000 之间")]
    public int MaxItems { get; set; } = 100;

    public DatabaseConfig Database { get; set; } = new();

    public FeatureFlagsConfig FeatureFlags { get; set; } = new();
}

public class DatabaseConfig
{
    [Required(ErrorMessage = "ConnectionString 不能为空")]
    public string ConnectionString { get; set; } = string.Empty;

    [Range(1, 300, ErrorMessage = "TimeoutSeconds 必须在 1 到 300 之间")]
    public int TimeoutSeconds { get; set; } = 30;
}

public class FeatureFlagsConfig
{
    public bool EnableNewUI { get; set; }

    public bool EnableLogging { get; set; } = true;
}
