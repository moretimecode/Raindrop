using System.Collections.Concurrent;
using System.Reflection;
using Raindrop.Data.Events;
using Raindrop.EventBus.Abstractions;

namespace Raindrop.Data.EFCore;

/// <summary>集中适配运行时事件类型，缓存委托并保留事件处理异常的原始身份。</summary>
internal static class EventBusDispatch
{
    private static readonly ConcurrentDictionary<Type, Func<IEventBus, IEvent, CancellationToken, Task>> Publishers = new();
    private static readonly MethodInfo PublishMethod = typeof(EventBusDispatch)
        .GetMethod(nameof(PublishTypedAsync), BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("无法找到事件发布适配方法。");

    public static Task PublishAsync(IEventBus eventBus, IEvent @event, CancellationToken cancellationToken)
        => Publishers.GetOrAdd(@event.GetType(), CreatePublisher)(eventBus, @event, cancellationToken);

    private static Func<IEventBus, IEvent, CancellationToken, Task> CreatePublisher(Type eventType)
        => PublishMethod.MakeGenericMethod(eventType)
            .CreateDelegate<Func<IEventBus, IEvent, CancellationToken, Task>>();

    private static Task PublishTypedAsync<TEvent>(IEventBus eventBus, IEvent @event, CancellationToken cancellationToken)
        where TEvent : IEvent
        => eventBus.PublishAsync((TEvent)@event, cancellationToken);
}
