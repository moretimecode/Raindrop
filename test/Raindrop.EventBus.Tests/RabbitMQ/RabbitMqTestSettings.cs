namespace Raindrop.AppConfiguration.Tests;

internal static class RabbitMqTestSettings
{
    public static string ConnectionString => ResolveConnectionString(
        Environment.GetEnvironmentVariable("RAINDROP_TEST_RABBITMQ_CONNECTION_STRING"));

    public static string ResolveConnectionString(string? configuredConnectionString)
    {
        return string.IsNullOrWhiteSpace(configuredConnectionString)
            ? "amqp://admin:admin123@localhost:5672"
            : configuredConnectionString;
    }
}
