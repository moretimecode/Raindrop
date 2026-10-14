using System.Globalization;
using System.Text;
using RabbitMQ.Client;

namespace Raindrop.EventBus.RabbitMQ;

public sealed class RabbitMqReceivedMessage : IRabbitMqReceivedMessage
{
    private readonly IChannel _channel;
    private readonly ulong _deliveryTag;
    private readonly byte[] _body;
    private readonly IReadOnlyBasicProperties _properties;
    private readonly string _retryQueue;
    private readonly SemaphoreSlim _syncRoot = new(1, 1);

    public RabbitMqReceivedMessage(
        IChannel channel,
        ulong deliveryTag,
        byte[] body,
        IReadOnlyBasicProperties properties,
        int failedCount,
        string retryQueue)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(properties);

        if (string.IsNullOrWhiteSpace(retryQueue))
        {
            throw new ArgumentException("RabbitMQ 重试队列不能为空。", nameof(retryQueue));
        }

        _channel = channel;
        _deliveryTag = deliveryTag;
        _body = body;
        _properties = properties;
        FailedCount = failedCount;
        _retryQueue = retryQueue;
    }

    public int FailedCount { get; }

    public static int ReadFailedCount(IReadOnlyBasicProperties properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        if (properties.Headers is null ||
            !properties.Headers.TryGetValue(RabbitMqMessageHeaders.FailedCount, out var value))
        {
            return 0;
        }

        return value switch
        {
            byte[] bytes => int.Parse(Encoding.UTF8.GetString(bytes), CultureInfo.InvariantCulture),
            int number => number,
            long number => Convert.ToInt32(number, CultureInfo.InvariantCulture),
            _ => Convert.ToInt32(value, CultureInfo.InvariantCulture)
        };
    }

    public async Task AckAsync(CancellationToken cancellationToken = default)
    {
        await _syncRoot.WaitAsync(cancellationToken);
        try
        {
            await _channel.BasicAckAsync(_deliveryTag, false, cancellationToken);
        }
        finally
        {
            _syncRoot.Release();
        }
    }

    public async Task RetryAsync(TimeSpan delay, CancellationToken cancellationToken = default)
    {
        await _syncRoot.WaitAsync(cancellationToken);
        try
        {
            var retryProperties = new BasicProperties();
            retryProperties.Persistent = true;
            CopyIfNotNull(_properties.ContentType, value => retryProperties.ContentType = value);
            CopyIfNotNull(_properties.ContentEncoding, value => retryProperties.ContentEncoding = value);
            CopyIfNotNull(_properties.Type, value => retryProperties.Type = value);
            CopyIfNotNull(_properties.MessageId, value => retryProperties.MessageId = value);
            CopyIfNotNull(_properties.CorrelationId, value => retryProperties.CorrelationId = value);
            retryProperties.Headers = CopyHeaders(_properties);
            retryProperties.Headers[RabbitMqMessageHeaders.FailedCount] =
                Encoding.UTF8.GetBytes((FailedCount + 1).ToString(CultureInfo.InvariantCulture));
            retryProperties.Expiration = Math.Max(1, Convert.ToInt32(delay.TotalMilliseconds, CultureInfo.InvariantCulture))
                .ToString(CultureInfo.InvariantCulture);

            await _channel.BasicPublishAsync(string.Empty, _retryQueue, false, retryProperties, _body, cancellationToken);
            await _channel.BasicAckAsync(_deliveryTag, false, cancellationToken);
        }
        finally
        {
            _syncRoot.Release();
        }
    }

    public async Task DeadLetterAsync(CancellationToken cancellationToken = default)
    {
        await _syncRoot.WaitAsync(cancellationToken);
        try
        {
            await _channel.BasicRejectAsync(_deliveryTag, false, cancellationToken);
        }
        finally
        {
            _syncRoot.Release();
        }
    }

    private static IDictionary<string, object?> CopyHeaders(IReadOnlyBasicProperties properties)
    {
        return properties.Headers is null
            ? new Dictionary<string, object?>()
            : new Dictionary<string, object?>(properties.Headers);
    }

    private static void CopyIfNotNull(string? value, Action<string> assign)
    {
        if (value is not null)
        {
            assign(value);
        }
    }
}
