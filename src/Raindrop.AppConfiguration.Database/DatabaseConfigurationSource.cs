using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Raindrop.AppConfiguration.Database;

/// <summary>
/// 数据库配置源的 <see cref="IConfigurationSource"/> 实现。
/// </summary>
public class DatabaseConfigurationSource : IConfigurationSource
{
    private readonly DatabaseConfigOptions _options;
    private readonly ILoggerFactory? _loggerFactory;

    public DatabaseConfigurationSource(DatabaseConfigOptions options, ILoggerFactory? loggerFactory = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _loggerFactory = loggerFactory;
    }

    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        return new DatabaseConfigurationProvider(_options, _loggerFactory ?? _options.LoggerFactory);
    }
}
