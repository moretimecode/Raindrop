using RabbitMQ.Client;

namespace Raindrop.EventBus.RabbitMQ;

/// <summary>
/// IConnection → IRabbitMqConnection 适配器。
/// </summary>
public sealed class RabbitMqConnectionAdapter : IRabbitMqConnection
{
    private readonly IConnection _connection;

    public RabbitMqConnectionAdapter(IConnection connection)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
    }

    public IRabbitMqChannel CreateChannel()
    {
        return new RabbitMqChannelAdapter(_connection.CreateModel());
    }
}
