using DTMS.DeliveryOrder.Application.Projections;
using DTMS.DeliveryOrder.IntegrationEvents;
using DTMS.SharedKernel.Projection;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DeliveryOrder.UnitTests;

public class OrderStatusHistoryProjectorTests
{
    [Fact]
    public async Task FirstEvent_AppendsRowWithNullFromStatus()
    {
        var (projector, store) = Build();
        var orderId = Guid.NewGuid();
        var evt = new DeliveryOrderConfirmedIntegrationEventV1(
            EventId: Guid.NewGuid(),
            OccurredOn: DateTime.UtcNow,
            DeliveryOrderId: orderId,
            Priority: "Normal",
            EarliestUtc: null, LatestUtc: null, SubmittedAt: null,
            Items: Array.Empty<ItemSummaryDto>());

        await projector.Consume(Ctx(evt));

        await store.Received(1).AppendAsync(
            OrderStatusHistoryProjector.Name,
            evt.EventId, orderId,
            fromStatus: null,
            toStatus: "Confirmed",
            evt.OccurredOn,
            reason: null,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SecondEvent_DerivesFromStatusFromLatestRow()
    {
        var (projector, store) = Build();
        var orderId = Guid.NewGuid();
        var t0 = DateTime.UtcNow.AddMinutes(-5);
        store.GetLatestForOrderAsync(orderId, Arg.Any<CancellationToken>())
            .Returns(("Confirmed", t0));

        var evt = new DeliveryOrderDispatchedIntegrationEventV1(
            Guid.NewGuid(), DateTime.UtcNow, orderId);

        await projector.Consume(Ctx(evt));

        await store.Received(1).AppendAsync(
            OrderStatusHistoryProjector.Name,
            evt.EventId, orderId,
            fromStatus: "Confirmed",
            toStatus: "Dispatched",
            evt.OccurredOn,
            reason: null,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DuplicateEvent_IsSkipped()
    {
        var (projector, store) = Build();
        var evt = new DeliveryOrderCancelledIntegrationEventV1(
            Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), "test");

        store.HasProcessedEventAsync(
                OrderStatusHistoryProjector.Name, evt.EventId, Arg.Any<CancellationToken>())
            .Returns(true);

        await projector.Consume(Ctx(evt));

        await store.DidNotReceive().AppendAsync(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(),
            Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<DateTime>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OutOfOrderEvent_IsSkipped()
    {
        var (projector, store) = Build();
        var orderId = Guid.NewGuid();
        var latestTime = DateTime.UtcNow;
        store.GetLatestForOrderAsync(orderId, Arg.Any<CancellationToken>())
            .Returns(("Dispatched", latestTime));

        // Old event arriving late
        var evt = new DeliveryOrderConfirmedIntegrationEventV1(
            EventId: Guid.NewGuid(),
            OccurredOn: latestTime.AddMinutes(-1),
            DeliveryOrderId: orderId,
            Priority: "Normal",
            EarliestUtc: null, LatestUtc: null, SubmittedAt: null,
            Items: Array.Empty<ItemSummaryDto>());

        await projector.Consume(Ctx(evt));

        await store.DidNotReceive().AppendAsync(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(),
            Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<DateTime>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancelledEvent_CarriesReasonIntoRow()
    {
        var (projector, store) = Build();
        var orderId = Guid.NewGuid();
        var evt = new DeliveryOrderCancelledIntegrationEventV1(
            Guid.NewGuid(), DateTime.UtcNow, orderId, "vendor rejected");

        await projector.Consume(Ctx(evt));

        await store.Received(1).AppendAsync(
            OrderStatusHistoryProjector.Name,
            evt.EventId, orderId,
            fromStatus: null, toStatus: "Cancelled",
            evt.OccurredOn, reason: "vendor rejected",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PartiallyCompletedEvent_BuildsReasonFromCounts()
    {
        var (projector, store) = Build();
        var orderId = Guid.NewGuid();
        var evt = new DeliveryOrderPartiallyCompletedIntegrationEventV1(
            Guid.NewGuid(), DateTime.UtcNow, orderId,
            DeliveredCount: 7, NotDeliveredCount: 3, TotalItems: 10);

        await projector.Consume(Ctx(evt));

        await store.Received(1).AppendAsync(
            OrderStatusHistoryProjector.Name,
            evt.EventId, orderId,
            fromStatus: null, toStatus: "PartiallyCompleted",
            evt.OccurredOn, reason: "7/10 delivered",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PermanentFailure_IsSwallowed_NotThrown()
    {
        var (projector, store) = Build();
        var evt = new DeliveryOrderConfirmedIntegrationEventV1(
            Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(),
            "Normal", null, null, null, Array.Empty<ItemSummaryDto>());

        store.When(s => s.AppendAsync(
                Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(),
                Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<DateTime>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("schema drift"));

        var act = async () => await projector.Consume(Ctx(evt));

        await act.Should().NotThrowAsync("permanent failures must not block the queue");
    }

    [Fact]
    public async Task TransientFailure_IsRethrown()
    {
        var (projector, store) = Build();
        var evt = new DeliveryOrderConfirmedIntegrationEventV1(
            Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(),
            "Normal", null, null, null, Array.Empty<ItemSummaryDto>());

        store.When(s => s.AppendAsync(
                Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(),
                Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<DateTime>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>()))
            .Do(_ => throw new TimeoutException("db lock"));

        var act = async () => await projector.Consume(Ctx(evt));

        await act.Should().ThrowAsync<TimeoutException>();
    }

    private static (OrderStatusHistoryProjector projector, IOrderStatusHistoryProjectionStore store) Build()
    {
        var store = Substitute.For<IOrderStatusHistoryProjectionStore>();
        store.HasProcessedEventAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(false);
        store.GetLatestForOrderAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(((string, DateTime)?)null);
        var metrics = new ProjectionMetrics();
        var projector = new OrderStatusHistoryProjector(
            store, metrics, new NoopOrderRealtimePublisher(), NullLogger<OrderStatusHistoryProjector>.Instance);
        return (projector, store);
    }

    private static ConsumeContext<T> Ctx<T>(T message) where T : class
    {
        var c = Substitute.For<ConsumeContext<T>>();
        c.Message.Returns(message);
        c.CancellationToken.Returns(CancellationToken.None);
        return c;
    }
}
