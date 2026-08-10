namespace Raindrop.EventBus.RabbitMQ;

public sealed class RabbitMqEventBusOptions
{
    public string ConnectionString { get; set; } = "amqp://guest:guest@localhost:5672";
}
