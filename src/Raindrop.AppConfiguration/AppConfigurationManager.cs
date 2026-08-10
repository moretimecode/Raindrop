using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Raindrop.AppConfiguration.Abstractions;

namespace Raindrop.AppConfiguration;

public class AppConfigurationManager<TOptions> : IAppConfigurationManager<TOptions> where TOptions : class
{
    private readonly IEnumerable<IConfigLoadErrorNotification<TOptions>> _notifications;
    private readonly IOptionsFactory<TOptions> _factory;
    private readonly List<IDisposable> _registrations = [];

    private volatile object? _syncObj;
    private TOptions? _config;

    public AppConfigurationManager(
        IOptionsFactory<TOptions> factory,
        IEnumerable<IOptionsChangeTokenSource<TOptions>> sources,
        IEnumerable<IConfigLoadErrorNotification<TOptions>> notifications)
    {
        _factory = factory;
        _notifications = notifications;

        void RegisterSource(IOptionsChangeTokenSource<TOptions> source)
        {
            IDisposable registration = ChangeToken.OnChange(
                      () => source.GetChangeToken(),
                      (name) => InvokeChanged(name),
                      source.Name);

            _registrations.Add(registration);
        }

        if (sources is IOptionsChangeTokenSource<TOptions>[] sourcesArray)
        {
            foreach (IOptionsChangeTokenSource<TOptions> source in sourcesArray)
            {
                RegisterSource(source);
            }
        }
        else
        {
            foreach (IOptionsChangeTokenSource<TOptions> source in sources)
            {
                RegisterSource(source);
            }
        }
    }

    private void InvokeChanged(string? name)
    {
        name ??= Options.DefaultName;
        try
        {
            _config = Get(name);
        }
        catch (Exception ex)
        {
            ConfigLoadError(ex);
        }
    }

    public TOptions Config
    {
        get
        {
            if (_config is not null)
            {
                return _config;
            }

            lock (_syncObj ?? Interlocked.CompareExchange(ref _syncObj, new object(), null) ?? _syncObj!)
            {
                return _config ??= Get();
            }
        }
    }

    private TOptions Get(string? name = default)
    {
        name ??= Options.DefaultName;
        return _factory.Create(name);
    }

    public void Dispose()
    {
        foreach (IDisposable registration in _registrations)
        {
            registration.Dispose();
        }

        _registrations.Clear();
    }

    private void ConfigLoadError(Exception ex)
    {
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        if (_notifications is null)
        {
            return;
        }

        foreach (var notification in _notifications)
        {
            try
            {
                notification.Send(ex);
            }
            catch (Exception exf)
            {
                Console.WriteLine($"配置加载错误通知失败: {notification.GetType().Name}, 异常: {exf}");
            }
        }
    }
}
