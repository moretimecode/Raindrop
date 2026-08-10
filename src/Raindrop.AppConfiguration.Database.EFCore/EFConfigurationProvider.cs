using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Raindrop.AppConfiguration;

namespace Microsoft.Extensions.Configuration;

public class EFConfigurationProvider<TDbContext, TEntity, TConfig>
    : ConfigurationProvider, IDisposable
    where TDbContext : DbContext
    where TEntity : class
    where TConfig : class
{
    private readonly ILogger _logger;
    private readonly EFConfigurationOptions<TDbContext, TEntity, TConfig> _options;
    private readonly IConfigurationKeyGenerator _keyGenerator;
    private readonly IEntityMapper<TEntity, TConfig> _mapper;
    private readonly Timer? _refreshTimer;
    private readonly object _lock = new();
    private List<TEntity>? _lastData;
    private Dictionary<string, string?> _previousData = new(StringComparer.OrdinalIgnoreCase);

    public EFConfigurationProvider(
        EFConfigurationOptions<TDbContext, TEntity, TConfig> options,
        ILoggerFactory? loggerFactory = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));

        if (options.DbContextFactory is null)
            throw new ArgumentException("DbContextFactory cannot be null.", nameof(options));
        if (options.Query is null)
            throw new ArgumentException("Query cannot be null.", nameof(options));
        if (options.Mapper is null && options.MapsterConfig is null)
            throw new ArgumentException("Either Mapper or MapsterConfig must be provided.", nameof(options));

        _logger = loggerFactory?.CreateLogger<EFConfigurationProvider<TDbContext, TEntity, TConfig>>()
                  ?? NullLoggerFactory.Instance.CreateLogger<EFConfigurationProvider<TDbContext, TEntity, TConfig>>();

        _keyGenerator = new DefaultConfigurationKeyGenerator(_options.SectionName);
        _mapper = options.Mapper is not null
            ? new DelegateEntityMapper<TEntity, TConfig>(options.Mapper)
            : new MapsterEntityMapper<TEntity, TConfig>(options.MapsterConfig!);

        Load();

        _refreshTimer = new Timer(OnTimerElapsed, null, _options.RefreshInterval, _options.RefreshInterval);
    }

    public override void Load()
    {
        try
        {
            var entities = LoadEntitiesFromDatabase();
            var configObject = _mapper.Map(entities);
            var configData = _keyGenerator.GenerateKeys(configObject);

            lock (_lock)
            {
                _lastData = entities.ToList();
                Data = configData;
                _previousData = new Dictionary<string, string?>(configData, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex)
        {
            HandleLoadFailure(ex, isInitialLoad: true);
        }
    }

    private List<TEntity> LoadEntitiesFromDatabase()
    {
        using var dbContext = _options.DbContextFactory!();
        var queryFunc = _options.Query!.Compile();
        var queryable = queryFunc(dbContext);
        return queryable.ToList();
    }

    private bool HasDataChanged(List<TEntity> newData)
    {
        if (_lastData is null) return true;

        if (_options.ChangeDetector is not null)
            return _options.ChangeDetector(_lastData, newData);

        return _lastData.Count != newData.Count ||
               !_lastData.SequenceEqual(newData, new EntityComparer<TEntity>());
    }

    private void OnTimerElapsed(object? state)
    {
        try
        {
            var entities = LoadEntitiesFromDatabase();

            if (!HasDataChanged(entities)) return;

            var configObject = _mapper.Map(entities);
            var configData = _keyGenerator.GenerateKeys(configObject);

            lock (_lock)
            {
                _lastData = entities.ToList();
                Data = configData;
                _previousData = new Dictionary<string, string?>(configData, StringComparer.OrdinalIgnoreCase);
            }

            OnReload();
        }
        catch (Exception ex)
        {
            HandleLoadFailure(ex, isInitialLoad: false);
        }
    }

    private void HandleLoadFailure(Exception exception, bool isInitialLoad)
    {
        var behavior = isInitialLoad
            ? _options.InitialLoadFailureBehavior
            : _options.ReloadFailureBehavior;

        var operation = isInitialLoad ? "加载" : "刷新";
        _logger.LogError(exception, "EF 配置{Operation}失败。", operation);
        NotifyLoadException(exception);

        if (behavior == ConfigurationLoadFailureBehavior.Throw)
        {
            throw new InvalidOperationException($"EF 配置{operation}失败。", exception);
        }
    }

    private void NotifyLoadException(Exception exception)
    {
        try
        {
            _options.OnLoadException?.Invoke(exception);
        }
        catch (Exception notificationException)
        {
            _logger.LogError(notificationException, "EF 配置加载失败通知回调异常。");
        }
    }

    public void Dispose()
    {
        _refreshTimer?.Dispose();
    }
}
