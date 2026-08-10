using System.Collections.Concurrent;
using Raindrop.EventBus.Abstractions;

namespace Raindrop.EventBus.Core;

public sealed class InMemoryConsumerIdempotencyStore : IConsumerIdempotencyStore
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new();

    public Task<ConsumerIdempotencyBeginResult> TryBeginAsync(
        string key,
        TimeSpan expiration,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.Add(expiration);

        while (true)
        {
            if (!_entries.TryGetValue(key, out var current))
            {
                var entry = new Entry(ConsumerIdempotencyBeginResult.Processing, expiresAt);

                if (_entries.TryAdd(key, entry))
                {
                    return Task.FromResult(ConsumerIdempotencyBeginResult.Started);
                }

                continue;
            }

            if (current.ExpiresAt <= now)
            {
                var entry = new Entry(ConsumerIdempotencyBeginResult.Processing, expiresAt);

                if (_entries.TryUpdate(key, entry, current))
                {
                    return Task.FromResult(ConsumerIdempotencyBeginResult.Started);
                }

                continue;
            }

            return Task.FromResult(current.Status);
        }
    }

    public Task MarkSucceededAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _entries.AddOrUpdate(
            key,
            static _ => new Entry(ConsumerIdempotencyBeginResult.Succeeded, DateTimeOffset.MaxValue),
            static (_, current) => current with { Status = ConsumerIdempotencyBeginResult.Succeeded });

        return Task.CompletedTask;
    }

    public Task MarkFailedAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _entries.TryRemove(key, out _);

        return Task.CompletedTask;
    }

    private sealed record Entry(
        ConsumerIdempotencyBeginResult Status,
        DateTimeOffset ExpiresAt);
}
