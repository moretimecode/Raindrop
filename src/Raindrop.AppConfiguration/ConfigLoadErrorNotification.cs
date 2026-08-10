namespace Raindrop.AppConfiguration;

internal class ConfigLoadErrorNotification<TOptions> : IConfigLoadErrorNotification<TOptions>
{
    private readonly Action<IServiceProvider, Exception> _handler;
    private readonly IServiceProvider _serviceProvider;
    public ConfigLoadErrorNotification(IServiceProvider serviceProvider, Action<IServiceProvider, Exception> handler)
    {
        _serviceProvider = serviceProvider;
        _handler = handler;
    }

    public void Send(Exception ex)
    {
        _handler(_serviceProvider, ex);
    }
}
