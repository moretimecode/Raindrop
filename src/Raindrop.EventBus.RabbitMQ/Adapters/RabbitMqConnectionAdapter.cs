using RabbitMQ.Client;

namespace Raindrop.EventBus.RabbitMQ;

/// <summary>
/// IConnection → IRabbitMqConnection 适配器。
/// </summary>
public sealed class RabbitMqConnectionAdapter : IRabbitMqConnection, IDisposable, IAsyncDisposable
{
    private IConnection? _connection;
    private readonly IConnectionFactory? _factory;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private bool _disposed;

    public RabbitMqConnectionAdapter(IConnection connection)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
    }

    /// <summary>
    /// 首次创建通道时异步建立连接，由适配器负责释放。
    /// </summary>
    public RabbitMqConnectionAdapter(IConnectionFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public async Task<IRabbitMqChannel> CreateChannelAsync(CancellationToken cancellationToken = default)
    {
        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _connection ??= await _factory!.CreateConnectionAsync(cancellationToken);
            return new RabbitMqChannelAdapter(await _connection.CreateChannelAsync(
                cancellationToken: cancellationToken));
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public void Dispose()
    {
        _connectionLock.Wait();
        try
        {
            if (_disposed) return;
            _disposed = true;
            if (_factory is not null) _connection?.Dispose();
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _connectionLock.WaitAsync();
        try
        {
            if (_disposed) return;
            _disposed = true;
            if (_factory is not null && _connection is not null)
                await _connection.DisposeAsync();
        }
        finally
        {
            _connectionLock.Release();
        }
    }
}
