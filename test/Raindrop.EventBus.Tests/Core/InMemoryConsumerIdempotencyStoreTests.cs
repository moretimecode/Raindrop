using Raindrop.EventBus.Abstractions;
using Raindrop.EventBus.Core;

namespace Raindrop.AppConfiguration.Tests;

public class InMemoryConsumerIdempotencyStoreTests
{
    [Fact]
    public async Task TryBeginAsync_NewKey_ReturnsStarted()
    {
        var store = new InMemoryConsumerIdempotencyStore();

        var result = await store.TryBeginAsync("key-1", TimeSpan.FromMinutes(1));

        Assert.Equal(ConsumerIdempotencyBeginResult.Started, result);
    }

    [Fact]
    public async Task TryBeginAsync_SameKeyWhileProcessing_ReturnsProcessing()
    {
        var store = new InMemoryConsumerIdempotencyStore();

        await store.TryBeginAsync("key-1", TimeSpan.FromMinutes(1));
        var result = await store.TryBeginAsync("key-1", TimeSpan.FromMinutes(1));

        Assert.Equal(ConsumerIdempotencyBeginResult.Processing, result);
    }

    [Fact]
    public async Task TryBeginAsync_AfterMarkSucceeded_ReturnsSucceeded()
    {
        var store = new InMemoryConsumerIdempotencyStore();

        await store.TryBeginAsync("key-1", TimeSpan.FromMinutes(1));
        await store.MarkSucceededAsync("key-1");
        var result = await store.TryBeginAsync("key-1", TimeSpan.FromMinutes(1));

        Assert.Equal(ConsumerIdempotencyBeginResult.Succeeded, result);
    }

    [Fact]
    public async Task TryBeginAsync_AfterMarkFailed_CanStartAgain()
    {
        var store = new InMemoryConsumerIdempotencyStore();

        await store.TryBeginAsync("key-1", TimeSpan.FromMinutes(1));
        await store.MarkFailedAsync("key-1");
        var result = await store.TryBeginAsync("key-1", TimeSpan.FromMinutes(1));

        Assert.Equal(ConsumerIdempotencyBeginResult.Started, result);
    }

    [Fact]
    public async Task TryBeginAsync_AfterExpiration_CanStartAgain()
    {
        var store = new InMemoryConsumerIdempotencyStore();

        await store.TryBeginAsync("key-1", TimeSpan.FromMilliseconds(10));
        await Task.Delay(50);
        var result = await store.TryBeginAsync("key-1", TimeSpan.FromMinutes(1));

        Assert.Equal(ConsumerIdempotencyBeginResult.Started, result);
    }

    [Fact]
    public async Task MarkSucceeded_UpdatesExistingKey()
    {
        var store = new InMemoryConsumerIdempotencyStore();

        await store.TryBeginAsync("key-1", TimeSpan.FromMinutes(1));
        await store.MarkSucceededAsync("key-1");

        var result = await store.TryBeginAsync("key-1", TimeSpan.FromMinutes(1));
        Assert.Equal(ConsumerIdempotencyBeginResult.Succeeded, result);
    }
}
