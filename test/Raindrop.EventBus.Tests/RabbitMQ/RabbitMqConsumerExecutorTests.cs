using Microsoft.Extensions.DependencyInjection;
using Raindrop.Data.Events;
using Raindrop.EventBus.Abstractions;
using Raindrop.EventBus.Core;
using Raindrop.EventBus.RabbitMQ;
using Raindrop.UnitOfWork.Abstractions;

namespace Raindrop.AppConfiguration.Tests;

public class RabbitMqConsumerExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_WithoutUnitOfWork_ReturnsSucceeded()
    {
        var provider = new ServiceCollection().AddScoped<SimpleHandler>().BuildServiceProvider();
        using var scope = provider.CreateScope();
        var executor = new RabbitMqConsumerExecutor(scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>());

        var result = await executor.ExecuteAsync<SimpleEvent, SimpleHandler>(new SimpleEvent(), CancellationToken.None);
        Assert.Equal(RabbitMqHandlerExecutionStatus.Succeeded, result.Status);
    }

    [Fact]
    public async Task ExecuteAsync_WhenHandlerThrows_ReturnsRetry()
    {
        var provider = new ServiceCollection().AddScoped<FailingHandler>().BuildServiceProvider();
        using var scope = provider.CreateScope();
        var executor = new RabbitMqConsumerExecutor(scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>());

        var result = await executor.ExecuteAsync<SimpleEvent, FailingHandler>(new SimpleEvent(), CancellationToken.None);
        Assert.Equal(RabbitMqHandlerExecutionStatus.Retry, result.Status);
        Assert.NotNull(result.Exception);
    }

    [Fact]
    public async Task ExecuteAsync_WithoutUnitOfWorkAttribute_DoesNotBeginUnitOfWork()
    {
        var uowFactory = new StubUnitOfWorkFactory();
        var provider = new ServiceCollection()
            .AddScoped<SimpleHandler>()
            .AddSingleton<IUnitOfWorkFactory>(uowFactory)
            .BuildServiceProvider();
        using var scope = provider.CreateScope();
        var executor = new RabbitMqConsumerExecutor(scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>());

        var result = await executor.ExecuteAsync<SimpleEvent, SimpleHandler>(new SimpleEvent(), CancellationToken.None);
        Assert.Equal(RabbitMqHandlerExecutionStatus.Succeeded, result.Status);
        Assert.Null(uowFactory.LastUnitOfWork);
    }

    [Fact]
    public async Task ExecuteAsync_WithUnitOfWorkAttribute_CommitsOnSuccess()
    {
        var uowFactory = new StubUnitOfWorkFactory();
        var provider = new ServiceCollection()
            .AddScoped<UnitOfWorkHandler>()
            .AddSingleton<IUnitOfWorkFactory>(uowFactory)
            .BuildServiceProvider();
        using var scope = provider.CreateScope();
        var executor = new RabbitMqConsumerExecutor(scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>());

        var result = await executor.ExecuteAsync<SimpleEvent, UnitOfWorkHandler>(new SimpleEvent(), CancellationToken.None);

        Assert.Equal(RabbitMqHandlerExecutionStatus.Succeeded, result.Status);
        Assert.NotNull(uowFactory.LastUnitOfWork);
        Assert.True(uowFactory.LastUnitOfWork.Committed);
    }

    [Fact]
    public async Task ExecuteAsync_WithIdempotentConsumer_WhenHandlerSucceeds_ReturnsSucceeded()
    {
        var store = new InMemoryConsumerIdempotencyStore();
        var provider = new ServiceCollection()
            .AddScoped<IdempotentHandler>()
            .AddSingleton<IConsumerIdempotencyStore>(store)
            .AddSingleton<IConsumerIdempotencyKeyResolver, PropertyPathConsumerIdempotencyKeyResolver>()
            .BuildServiceProvider();
        using var scope = provider.CreateScope();
        var executor = new RabbitMqConsumerExecutor(scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>());

        var result = await executor.ExecuteAsync<SimpleEvent, IdempotentHandler>(new SimpleEvent(), CancellationToken.None);
        Assert.Equal(RabbitMqHandlerExecutionStatus.Succeeded, result.Status);
    }

    [Fact]
    public async Task ExecuteAsync_WithIdempotentConsumer_WhenKeyAlreadySucceeded_ReturnsAlreadyProcessed()
    {
        var store = new InMemoryConsumerIdempotencyStore();
        var eventData = new SimpleEvent("already-done");
        var key = $"{typeof(IdempotentHandler).FullName}:{eventData.Id}";
        await store.MarkSucceededAsync(key);
        var provider = new ServiceCollection()
            .AddScoped<IdempotentHandler>()
            .AddSingleton<IConsumerIdempotencyStore>(store)
            .AddSingleton<IConsumerIdempotencyKeyResolver, PropertyPathConsumerIdempotencyKeyResolver>()
            .BuildServiceProvider();
        using var scope = provider.CreateScope();
        var executor = new RabbitMqConsumerExecutor(scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>());

        var result = await executor.ExecuteAsync<SimpleEvent, IdempotentHandler>(eventData, CancellationToken.None);

        Assert.Equal(RabbitMqHandlerExecutionStatus.AlreadyProcessed, result.Status);
    }

    [Fact]
    public async Task ExecuteAsync_WithUnitOfWork_WhenHandlerFails_RollsBack()
    {
        var uowFactory = new StubUnitOfWorkFactory();
        var provider = new ServiceCollection()
            .AddScoped<FailingUnitOfWorkHandler>()
            .AddSingleton<IUnitOfWorkFactory>(uowFactory)
            .BuildServiceProvider();
        using var scope = provider.CreateScope();
        var executor = new RabbitMqConsumerExecutor(scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>());

        var result = await executor.ExecuteAsync<SimpleEvent, FailingUnitOfWorkHandler>(new SimpleEvent(), CancellationToken.None);
        Assert.Equal(RabbitMqHandlerExecutionStatus.Retry, result.Status);
        Assert.True(uowFactory.LastUnitOfWork!.RolledBack);
    }

    // ===== 测试用的 Handler =====

    [Fact]
    public async Task ExecuteAsync_WhenRollbackFails_StillReturnsRetry()
    {
        var provider = new ServiceCollection()
            .AddScoped<FailingUnitOfWorkHandler>()
            .AddSingleton<IUnitOfWorkFactory, ThrowingRollbackUnitOfWorkFactory>()
            .BuildServiceProvider();
        using var scope = provider.CreateScope();
        var executor = new RabbitMqConsumerExecutor(scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>());

        var result = await executor.ExecuteAsync<SimpleEvent, FailingUnitOfWorkHandler>(new SimpleEvent(), CancellationToken.None);

        Assert.Equal(RabbitMqHandlerExecutionStatus.Retry, result.Status);
    }

    [Fact]
    public async Task ExecuteAsync_WhenMarkFailedFails_StillReturnsRetry()
    {
        var provider = new ServiceCollection()
            .AddScoped<FailingIdempotentHandler>()
            .AddSingleton<IConsumerIdempotencyStore, ThrowingMarkFailedStore>()
            .AddSingleton<IConsumerIdempotencyKeyResolver, PropertyPathConsumerIdempotencyKeyResolver>()
            .BuildServiceProvider();
        using var scope = provider.CreateScope();
        var executor = new RabbitMqConsumerExecutor(scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>());

        var result = await executor.ExecuteAsync<SimpleEvent, FailingIdempotentHandler>(
            new SimpleEvent("mark-failed"),
            CancellationToken.None);

        Assert.Equal(RabbitMqHandlerExecutionStatus.Retry, result.Status);
    }

    private sealed record SimpleEvent(string Id = "simple") : IEvent;

    private sealed class SimpleHandler : IEventHandler<SimpleEvent>
    {
        public Task HandleAsync(SimpleEvent eventData, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class FailingHandler : IEventHandler<SimpleEvent>
    {
        public Task HandleAsync(SimpleEvent eventData, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("预期失败。");
    }

    [UnitOfWork]
    private sealed class UnitOfWorkHandler : IEventHandler<SimpleEvent>
    {
        public Task HandleAsync(SimpleEvent eventData, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    [UnitOfWork]
    private sealed class FailingUnitOfWorkHandler : IEventHandler<SimpleEvent>
    {
        public Task HandleAsync(SimpleEvent eventData, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("预期失败。");
    }

    [IdempotentConsumer("Id", ExpireSeconds = 30)]
    private sealed class IdempotentHandler : IEventHandler<SimpleEvent>
    {
        public Task HandleAsync(SimpleEvent eventData, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    [IdempotentConsumer("Id", ExpireSeconds = 30)]
    private sealed class FailingIdempotentHandler : IEventHandler<SimpleEvent>
    {
        public Task HandleAsync(SimpleEvent eventData, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("预期失败。");
    }

    // ===== Stub =====

    private sealed class StubUnitOfWorkFactory : IUnitOfWorkFactory
    {
        public StubUnitOfWork? LastUnitOfWork { get; private set; }

        public ValueTask<IUnitOfWork> BeginAsync(CancellationToken cancellationToken = default)
        {
            LastUnitOfWork = new StubUnitOfWork();
            return ValueTask.FromResult<IUnitOfWork>(LastUnitOfWork);
        }
    }

    private sealed class StubUnitOfWork : IUnitOfWork
    {
        public bool RolledBack { get; private set; }
        public bool Committed { get; private set; }

        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            Committed = true;
            return Task.CompletedTask;
        }

        public Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            RolledBack = true;
            return Task.CompletedTask;
        }

        public void OnCompleted(Func<Task> handler) { }

    public void ClearCompletedHandler() { }



    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ThrowingRollbackUnitOfWorkFactory : IUnitOfWorkFactory
    {
        public ValueTask<IUnitOfWork> BeginAsync(CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult<IUnitOfWork>(new ThrowingRollbackUnitOfWork());
        }
    }

    private sealed class ThrowingRollbackUnitOfWork : IUnitOfWork
    {
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RollbackAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("回滚失败。");

        public void OnCompleted(Func<Task> handler) { }

        public void ClearCompletedHandler() { }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ThrowingMarkFailedStore : IConsumerIdempotencyStore
    {
        public Task<ConsumerIdempotencyBeginResult> TryBeginAsync(
            string key,
            TimeSpan expiration,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ConsumerIdempotencyBeginResult.Started);
        }

        public Task MarkSucceededAsync(string key, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task MarkFailedAsync(string key, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("幂等失败标记写入失败。");
    }
}
