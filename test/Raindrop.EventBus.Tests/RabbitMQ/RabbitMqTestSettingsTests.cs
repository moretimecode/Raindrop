namespace Raindrop.AppConfiguration.Tests;

public class RabbitMqTestSettingsTests
{
    [Theory]
    [InlineData(null, "amqp://admin:admin123@localhost:5672")]
    [InlineData("", "amqp://admin:admin123@localhost:5672")]
    [InlineData("amqp://test-user:temporary-password@localhost:5673", "amqp://test-user:temporary-password@localhost:5673")]
    public void ResolveConnectionString_UsesConfiguredBrokerOrLocalDefault(string? configured, string expected)
    {
        Assert.Equal(expected, RabbitMqTestSettings.ResolveConnectionString(configured));
    }
}
