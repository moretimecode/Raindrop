namespace Raindrop.EventBus.RabbitMQ;

public interface IRabbitMqDeadLetterRetryPolicyResolver
{
    RabbitMqDeadLetterRetryPolicy Resolve(Type handlerType);
}
