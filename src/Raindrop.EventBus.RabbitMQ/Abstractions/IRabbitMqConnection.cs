namespace Raindrop.EventBus.RabbitMQ;

/// <summary>
/// 对 RabbitMQ IConnection 的薄封装，方便单元测试 mock。
/// </summary>
public interface IRabbitMqConnection
{
    IRabbitMqChannel CreateChannel();
}
