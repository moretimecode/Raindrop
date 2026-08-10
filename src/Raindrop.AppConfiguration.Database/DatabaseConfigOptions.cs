using System.Data;
using Microsoft.Extensions.Logging;

namespace Raindrop.AppConfiguration.Database;

public class DatabaseConfigOptions
{
    public Func<IDbConnection> BuildConnection { get; set; } = null!;

    public string Sql { get; set; } = null!;

    public string SectionName { get; set; } = "DbConfig";

    public bool ReloadOnChange { get; set; } = true;

    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromSeconds(5);

    public bool UsePascalNaming { get; set; } = true;

    public ConfigurationLoadFailureBehavior InitialLoadFailureBehavior { get; set; } =
        ConfigurationLoadFailureBehavior.Throw;

    public ConfigurationLoadFailureBehavior ReloadFailureBehavior { get; set; } =
        ConfigurationLoadFailureBehavior.KeepLastKnownGood;

    public Action<Exception>? OnLoadException { get; set; }

    public bool StrictJsonParsing { get; set; }

    /// <summary>
    /// 可选的日志工厂。为 null 时不输出日志。
    /// </summary>
    public ILoggerFactory? LoggerFactory { get; set; }
}
