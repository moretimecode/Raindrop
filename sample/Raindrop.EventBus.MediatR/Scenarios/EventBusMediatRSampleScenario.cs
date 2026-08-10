using Microsoft.Extensions.DependencyInjection;
using Raindrop.Data.Events;
using Raindrop.EventBus.Abstractions;

namespace Raindrop.AppConfiguration.Sample.Scenarios;

/// <summary>
/// 演示本地事件总线（MediatR）：
///   - 定义 IEvent 事件
///   - 实现 IEventHandler<TEvent>
///   - IEventBus.PublishAsync 发布
///   - MediatREventNotificationHandler 自动分发给所有 Handler
/// </summary>
public static class EventBusMediatRSampleScenario
{
    public static async Task RunAsync()
    {
        Console.WriteLine("===== MediatR 本地事件总线示例 =====");

        var services = new ServiceCollection();
        services.AddLogging();

        // 注册事件总线（扫描当前程序集的 IEventHandler 实现）
        services.AddMediatRLocalEventBus(typeof(EventBusMediatRSampleScenario).Assembly);

        await using var provider = services.BuildServiceProvider();
        var eventBus = provider.GetRequiredService<IEventBus>();

        // 发布事件 → RoutingEventBus → MediatR Transport → 所有 IEventHandler<OrderCreated>
        Console.WriteLine("  发布 OrderCreated 事件...");
        await eventBus.PublishAsync(new OrderCreated(Guid.NewGuid(), "ORD-EB-001"));

        // 发布另一个事件
        Console.WriteLine("  发布 PaymentReceived 事件...");
        await eventBus.PublishAsync(new PaymentReceived(Guid.NewGuid(), 99.90m));

        Console.WriteLine();
    }

    // ===== 事件定义 =====

    public sealed record OrderCreated(Guid OrderId, string OrderNumber) : IEvent;

    public sealed record PaymentReceived(Guid PaymentId, decimal Amount) : IEvent;

    // ===== 事件处理器 =====

    public sealed class OrderCreatedHandler : IEventHandler<OrderCreated>
    {
        public Task HandleAsync(OrderCreated eventData, CancellationToken cancellationToken = default)
        {
            Console.WriteLine($"    → OrderCreatedHandler: 订单 {eventData.OrderNumber} 已创建");
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// 同一事件可以有多个处理器，MediatR 会依次调用。
    /// </summary>
    public sealed class OrderCreatedNotificationHandler : IEventHandler<OrderCreated>
    {
        public Task HandleAsync(OrderCreated eventData, CancellationToken cancellationToken = default)
        {
            Console.WriteLine($"    → OrderCreatedNotificationHandler: 发送通知（订单 {eventData.OrderNumber}）");
            return Task.CompletedTask;
        }
    }

    public sealed class PaymentReceivedHandler : IEventHandler<PaymentReceived>
    {
        public Task HandleAsync(PaymentReceived eventData, CancellationToken cancellationToken = default)
        {
            Console.WriteLine($"    → PaymentReceivedHandler: 收到付款 {eventData.Amount:C}");
            return Task.CompletedTask;
        }
    }
}
