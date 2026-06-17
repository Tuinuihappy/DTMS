using AMR.DeliveryPlanning.Dispatch.Application.Projections;
using AMR.DeliveryPlanning.Dispatch.IntegrationEvents;
using AMR.DeliveryPlanning.SharedKernel.Projection;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Dispatch.UnitTests;

public class TripItemsProjectorTests
{
    [Fact]
    public async Task TripStarted_WithItems_InsertsOneRowPerSnapshot()
    {
        var (projector, store) = Build();
        var tripId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var occurredOn = DateTime.UtcNow;
        var items = new List<TripItemSnapshot>
        {
            Snap(orderId, "LOT-A", 1),
            Snap(orderId, "LOT-B", 2),
        };
        var evt = new TripStartedIntegrationEvent(
            Guid.NewGuid(), occurredOn, tripId,
            JobId: Guid.Empty, VehicleId: Guid.Empty, DeliveryOrderId: orderId,
            Items: items);

        await projector.Consume(Ctx(evt));

        await store.Received(1).InsertBindingsAsync(
            TripItemsProjector.Name,
            evt.EventId, tripId, occurredOn,
            items,
            Arg.Any<CancellationToken>());
        await store.DidNotReceive().RecordEmptyBindingAsync(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(),
            Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TripStarted_WithEmptyItems_RecordsInboxWithoutInsertingRows()
    {
        var (projector, store) = Build();
        var tripId = Guid.NewGuid();
        var evt = new TripStartedIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, tripId,
            JobId: Guid.Empty, VehicleId: Guid.Empty, DeliveryOrderId: Guid.NewGuid(),
            Items: Array.Empty<TripItemSnapshot>());

        await projector.Consume(Ctx(evt));

        await store.Received(1).RecordEmptyBindingAsync(
            TripItemsProjector.Name,
            evt.EventId, tripId, evt.OccurredOn,
            Arg.Any<CancellationToken>());
        await store.DidNotReceive().InsertBindingsAsync(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(),
            Arg.Any<DateTime>(), Arg.Any<IReadOnlyList<TripItemSnapshot>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TripStarted_WithNullItems_IsTreatedAsEmpty()
    {
        var (projector, store) = Build();
        var evt = new TripStartedIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(),
            JobId: Guid.Empty, VehicleId: Guid.Empty, DeliveryOrderId: Guid.NewGuid(),
            Items: null);

        await projector.Consume(Ctx(evt));

        await store.Received(1).RecordEmptyBindingAsync(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(),
            Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DuplicateTripStarted_IsNoop()
    {
        var (projector, store) = Build();
        store.HasProcessedEventAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(true);
        var evt = new TripStartedIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(),
            JobId: Guid.Empty, VehicleId: Guid.Empty, DeliveryOrderId: Guid.NewGuid(),
            Items: new List<TripItemSnapshot> { Snap(Guid.NewGuid(), "LOT-A", 1) });

        await projector.Consume(Ctx(evt));

        await store.DidNotReceive().InsertBindingsAsync(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(),
            Arg.Any<DateTime>(), Arg.Any<IReadOnlyList<TripItemSnapshot>>(),
            Arg.Any<CancellationToken>());
        await store.DidNotReceive().RecordEmptyBindingAsync(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(),
            Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TripPickupCompleted_FlipsItemStatusToPicked()
    {
        var (projector, store) = Build();
        var tripId = Guid.NewGuid();
        var evt = new TripPickupCompletedIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, tripId, DeliveryOrderId: Guid.NewGuid());

        await projector.Consume(Ctx(evt));

        await store.Received(1).UpdateItemStatusForTripAsync(
            TripItemsProjector.Name,
            evt.EventId, tripId,
            newItemStatus: "Picked",
            evt.OccurredOn,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TripDropCompleted_PodRequired_FlipsItemStatusToDroppedOff()
    {
        var (projector, store) = Build();
        var tripId = Guid.NewGuid();
        var evt = new TripDropCompletedIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, tripId, DeliveryOrderId: Guid.NewGuid(),
            RequiresDropPod: true);

        await projector.Consume(Ctx(evt));

        await store.Received(1).UpdateItemStatusForTripAsync(
            TripItemsProjector.Name,
            evt.EventId, tripId,
            newItemStatus: "DroppedOff",
            evt.OccurredOn,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TripDropCompleted_NoPodRequired_FlipsItemStatusStraightToDelivered()
    {
        // V1.2 — when the order doesn't require POD, drop is the delivery
        // moment. Projector skips the DroppedOff interstitial so
        // dispatch.TripItems matches what DeliveryOrder did on its side.
        var (projector, store) = Build();
        var tripId = Guid.NewGuid();
        var evt = new TripDropCompletedIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, tripId, DeliveryOrderId: Guid.NewGuid(),
            RequiresDropPod: false);

        await projector.Consume(Ctx(evt));

        await store.Received(1).UpdateItemStatusForTripAsync(
            TripItemsProjector.Name,
            evt.EventId, tripId,
            newItemStatus: "Delivered",
            evt.OccurredOn,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TripDropCompleted_NullPodFlag_FallsBackToDroppedOff()
    {
        // Pre-V1.2 in-flight events have null RequiresDropPod. Projector
        // stays on the legacy DroppedOff path so a later TripCompleted
        // still finalizes the row at Delivered — no silent state skip.
        var (projector, store) = Build();
        var tripId = Guid.NewGuid();
        var evt = new TripDropCompletedIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, tripId, DeliveryOrderId: Guid.NewGuid(),
            RequiresDropPod: null);

        await projector.Consume(Ctx(evt));

        await store.Received(1).UpdateItemStatusForTripAsync(
            TripItemsProjector.Name,
            evt.EventId, tripId,
            newItemStatus: "DroppedOff",
            evt.OccurredOn,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TripCompleted_FlipsItemStatusToDelivered()
    {
        var (projector, store) = Build();
        var tripId = Guid.NewGuid();
        var evt = new TripCompletedIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, tripId,
            JobId: Guid.NewGuid(), DeliveryOrderId: Guid.NewGuid(),
            VendorUpperKey: "U-1");

        await projector.Consume(Ctx(evt));

        await store.Received(1).UpdateItemStatusForTripAsync(
            TripItemsProjector.Name,
            evt.EventId, tripId,
            newItemStatus: "Delivered",
            evt.OccurredOn,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TripFailed_FlipsItemStatusToUnbound()
    {
        var (projector, store) = Build();
        var tripId = Guid.NewGuid();
        var evt = new TripFailedIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, tripId,
            JobId: Guid.NewGuid(), DeliveryOrderId: Guid.NewGuid(),
            Reason: "robot offline", VendorUpperKey: "U-1");

        await projector.Consume(Ctx(evt));

        await store.Received(1).UpdateItemStatusForTripAsync(
            TripItemsProjector.Name,
            evt.EventId, tripId,
            newItemStatus: "Unbound",
            evt.OccurredOn,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TripCancelled_FlipsItemStatusToUnbound()
    {
        var (projector, store) = Build();
        var tripId = Guid.NewGuid();
        var evt = new TripCancelledIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, tripId,
            JobId: Guid.NewGuid(), DeliveryOrderId: Guid.NewGuid(),
            Reason: "operator abort", VendorUpperKey: null);

        await projector.Consume(Ctx(evt));

        await store.Received(1).UpdateItemStatusForTripAsync(
            TripItemsProjector.Name,
            evt.EventId, tripId,
            newItemStatus: "Unbound",
            evt.OccurredOn,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PermanentFailure_IsSwallowed()
    {
        var (projector, store) = Build();
        var evt = new TripCompletedIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(),
            JobId: Guid.NewGuid(), DeliveryOrderId: Guid.NewGuid(),
            VendorUpperKey: "U-1");

        store.When(s => s.UpdateItemStatusForTripAsync(
                Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(),
                Arg.Any<string>(), Arg.Any<DateTime>(),
                Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("schema drift"));

        var act = async () => await projector.Consume(Ctx(evt));
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task TransientFailure_IsRethrown()
    {
        var (projector, store) = Build();
        var evt = new TripCompletedIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(),
            JobId: Guid.NewGuid(), DeliveryOrderId: Guid.NewGuid(),
            VendorUpperKey: "U-1");

        store.When(s => s.UpdateItemStatusForTripAsync(
                Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(),
                Arg.Any<string>(), Arg.Any<DateTime>(),
                Arg.Any<CancellationToken>()))
            .Do(_ => throw new TimeoutException("db lock"));

        var act = async () => await projector.Consume(Ctx(evt));
        await act.Should().ThrowAsync<TimeoutException>();
    }

    private static (TripItemsProjector projector, ITripItemsProjectionStore store) Build()
    {
        var store = Substitute.For<ITripItemsProjectionStore>();
        store.HasProcessedEventAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(false);
        var metrics = new ProjectionMetrics();
        var projector = new TripItemsProjector(
            store, metrics, NullLogger<TripItemsProjector>.Instance);
        return (projector, store);
    }

    private static TripItemSnapshot Snap(Guid orderId, string lot, int seq)
        => new(
            ItemPk: Guid.NewGuid(),
            ItemSeq: seq,
            LotNo: lot,
            ItemStatus: "Pending",
            PickupCode: "ST-A",
            DropCode: "ST-B",
            WeightKg: 10,
            DeliveryOrderId: orderId,
            OrderRef: "OD-001",
            OrderStatus: "Dispatched");

    private static ConsumeContext<T> Ctx<T>(T message) where T : class
    {
        var c = Substitute.For<ConsumeContext<T>>();
        c.Message.Returns(message);
        c.CancellationToken.Returns(CancellationToken.None);
        return c;
    }
}
