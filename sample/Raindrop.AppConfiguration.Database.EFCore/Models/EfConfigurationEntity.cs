namespace Raindrop.AppConfiguration.Sample.Models;

public class EfConfigurationEntity
{
    public int Id { get; set; }

    public string AppName { get; set; } = string.Empty;

    public int MaxItems { get; set; }

    public bool EnableNewUI { get; set; }
}
