using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.Configuration;

/// <summary>
/// EF Core 配置源的 <see cref="IConfigurationSource"/> 实现。
/// </summary>
public class EFConfigurationSource<TDbContext, TEntity, TConfig> : IConfigurationSource
    where TDbContext : DbContext
    where TEntity : class
    where TConfig : class
{
    private readonly EFConfigurationOptions<TDbContext, TEntity, TConfig> _options;
    private readonly ILoggerFactory? _loggerFactory;

    public EFConfigurationSource(
        EFConfigurationOptions<TDbContext, TEntity, TConfig> options,
        ILoggerFactory? loggerFactory = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _loggerFactory = loggerFactory;
    }

    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        return new EFConfigurationProvider<TDbContext, TEntity, TConfig>(
            _options,
            _loggerFactory ?? _options.LoggerFactory);
    }
}
