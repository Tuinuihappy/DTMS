using DTMS.DeliveryOrder.Application.Projections;
using DTMS.DeliveryOrder.IntegrationEvents;
using DTMS.Dispatch.IntegrationEvents;
using DTMS.Planning.IntegrationEvents;
using DTMS.SharedKernel.Projection;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DeliveryOrder.UnitTests;

// Phase P4.6 — projector is now a thin trigger that hands every Order
// event to RefreshFromAggregateAsync. Tests assert the wiring (right
// orderId + occurredOn forwarded), not field-by-field payload mapping —
// that mapping now lives in the store and is covered by store tests.
public class OrderListViewProjectorTests
{
    [Fact]
    public async Task Created_TriggersRefresh()
    {
        var (projector, store) = Build();
        var orderId = Guid.NewGuid();
        var evt = new DeliveryOrderCreatedIntegrationEventV1(
            EventId: Guid.NewGuid(), OccurredOn: DateTime.UtcNow, DeliveryOrderId: orderId,
            OrderRef: "ORD-42", SourceSystem: "Sap", Status: "Draft", Priority: "High",
            RequestedTransportMode: "Amr",
            RequestedBy: "alice", CreatedBy: "system", Notes: "rush",
            EarliestUtc: null, LatestUtc: null, SubmittedAt: null,
            RequiresDropPod: true, RequiresPickupPod: false,
            TotalItems: 2, TotalQuantity: 7, TotalWeightKg: 8.0,
            Items: Array.Empty<ItemSummaryDto>());

        await projector.Consume(Ctx(evt));

        await store.Received(1).RefreshFromAggregateAsync(
            orderId, evt.OccurredOn, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Submitted_TriggersRefresh()
    {
        var (projector, store) = Build();
        var orderId = Guid.NewGuid();
        var evt = new DeliveryOrderSubmittedIntegrationEventV1(
            Guid.NewGuid(), DateTime.UtcNow, orderId);

        await projector.Consume(Ctx(evt));

        await store.Received(1).RefreshFromAggregateAsync(
            orderId, evt.OccurredOn, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Validated_TriggersRefresh()
    {
        var (projector, store) = Build();
        var orderId = Guid.NewGuid();
        var evt = new DeliveryOrderValidatedIntegrationEventV1(
            Guid.NewGuid(), DateTime.UtcNow, orderId);

        await projector.Consume(Ctx(evt));

        await store.Received(1).RefreshFromAggregateAsync(
            orderId, evt.OccurredOn, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Confirmed_TriggersRefresh()
    {
        var (projector, store) = Build();
        var orderId = Guid.NewGuid();
        var evt = new DeliveryOrderConfirmedIntegrationEventV1(
            Guid.NewGuid(), DateTime.UtcNow, orderId,
            "High", null, null, null, Array.Empty<ItemSummaryDto>());

        await projector.Consume(Ctx(evt));

        await store.Received(1).RefreshFromAggregateAsync(
            orderId, evt.OccurredOn, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Failed_TriggersRefresh()
    {
        var (projector, store) = Build();
        var orderId = Guid.NewGuid();
        var evt = new DeliveryOrderFailedIntegrationEventV1(
            Guid.NewGuid(), DateTime.UtcNow, orderId, "vendor rejected");

        await projector.Consume(Ctx(evt));

        await store.Received(1).RefreshFromAggregateAsync(
            orderId, evt.OccurredOn, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Released_TriggersRefresh()
    {
        var (projector, store) = Build();
        var orderId = Guid.NewGuid();
        var evt = new DeliveryOrderReleasedIntegrationEventV1(
            Guid.NewGuid(), DateTime.UtcNow, orderId);

        await projector.Consume(Ctx(evt));

        await store.Received(1).RefreshFromAggregateAsync(
            orderId, evt.OccurredOn, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Amended_TriggersRefresh()
    {
        var (projector, store) = Build();
        var orderId = Guid.NewGuid();
        var evt = new DeliveryOrderAmendedIntegrationEventV1(
            Guid.NewGuid(), DateTime.UtcNow, orderId, "service window slipped");

        await projector.Consume(Ctx(evt));

        await store.Received(1).RefreshFromAggregateAsync(
            orderId, evt.OccurredOn, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DraftUpdated_TriggersRefresh()
    {
        var (projector, store) = Build();
        var orderId = Guid.NewGuid();
        var evt = new DeliveryOrderDraftUpdatedIntegrationEventV1(
            Guid.NewGuid(), DateTime.UtcNow, orderId);

        await projector.Consume(Ctx(evt));

        await store.Received(1).RefreshFromAggregateAsync(
            orderId, evt.OccurredOn, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TripFailed_SetsHasFailedTripTrue()
    {
        var (projector, store) = Build();
        var orderId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var evt = new TripFailedIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, tripId,
            JobId: Guid.NewGuid(), DeliveryOrderId: orderId,
            Reason: "vendor", VendorUpperKey: "UK");

        await projector.Consume(Ctx(evt));

        await store.Received(1).SetTripDerivedFieldsAsync(
            orderId, hasFailedTrip: true, latestTripId: tripId,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TripCompleted_ClearsHasFailedTripFlag()
    {
        var (projector, store) = Build();
        var orderId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var evt = new TripCompletedIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, tripId,
            JobId: Guid.NewGuid(), DeliveryOrderId: orderId,
            VendorUpperKey: "UK");

        await projector.Consume(Ctx(evt));

        await store.Received(1).SetTripDerivedFieldsAsync(
            orderId, hasFailedTrip: false, latestTripId: tripId,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task JobDispatched_SetsHasActiveJobTrue()
    {
        var (projector, store) = Build();
        var orderId = Guid.NewGuid();
        var evt = new JobDispatchedIntegrationEventV1(
            Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), orderId,
            TripId: Guid.NewGuid(), VendorOrderKey: "VK", AttemptNumber: 1);

        await projector.Consume(Ctx(evt));

        await store.Received(1).SetJobDerivedFieldsAsync(
            orderId, hasActiveJob: true, latestJobStatus: "Dispatched",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task JobCompleted_ClearsHasActiveJobFlag()
    {
        var (projector, store) = Build();
        var orderId = Guid.NewGuid();
        var evt = new JobCompletedIntegrationEventV1(
            Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), orderId, TripId: Guid.NewGuid());

        await projector.Consume(Ctx(evt));

        await store.Received(1).SetJobDerivedFieldsAsync(
            orderId, hasActiveJob: false, latestJobStatus: "Completed",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DuplicateEvent_IsSkipped()
    {
        var (projector, store) = Build();
        var evt = new DeliveryOrderFailedIntegrationEventV1(
            Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), "x");

        store.HasProcessedEventAsync(
                OrderListViewProjector.Name, evt.EventId, Arg.Any<CancellationToken>())
            .Returns(true);

        await projector.Consume(Ctx(evt));

        await store.DidNotReceive().RefreshFromAggregateAsync(
            Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PermanentFailure_IsSwallowed()
    {
        var (projector, store) = Build();
        var evt = new DeliveryOrderFailedIntegrationEventV1(
            Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), "x");

        store.When(s => s.RefreshFromAggregateAsync(
                Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("schema drift"));

        var act = async () => await projector.Consume(Ctx(evt));
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task TransientFailure_IsRethrown()
    {
        var (projector, store) = Build();
        var evt = new DeliveryOrderFailedIntegrationEventV1(
            Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), "x");

        store.When(s => s.RefreshFromAggregateAsync(
                Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()))
            .Do(_ => throw new TimeoutException("db lock"));

        var act = async () => await projector.Consume(Ctx(evt));
        await act.Should().ThrowAsync<TimeoutException>();
    }

    private static (OrderListViewProjector projector, IOrderListViewProjectionStore store) Build()
    {
        var store = Substitute.For<IOrderListViewProjectionStore>();
        store.HasProcessedEventAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(false);
        var metrics = new ProjectionMetrics();
        var projector = new OrderListViewProjector(
            store, metrics, new NoopOrderRealtimePublisher(),
            NullLogger<OrderListViewProjector>.Instance);
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
