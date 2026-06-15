using AMR.DeliveryPlanning.DeliveryOrder.Application.Projections;
using AMR.DeliveryPlanning.DeliveryOrder.IntegrationEvents;
using AMR.DeliveryPlanning.Dispatch.IntegrationEvents;
using AMR.DeliveryPlanning.Planning.IntegrationEvents;
using AMR.DeliveryPlanning.SharedKernel.Projection;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DeliveryOrder.UnitTests;

public class OrderListViewProjectorTests
{
    [Fact]
    public async Task Created_UpsertsRowWithFullPayload()
    {
        var (projector, store) = Build();
        var orderId = Guid.NewGuid();
        var item1 = new ItemSummaryDto("SKU-001", 5.0, Guid.NewGuid(), Guid.NewGuid());
        var item2 = new ItemSummaryDto("SKU-002", 3.0, Guid.NewGuid(), Guid.NewGuid());
        var evt = new DeliveryOrderCreatedIntegrationEventV1(
            EventId: Guid.NewGuid(), OccurredOn: DateTime.UtcNow, DeliveryOrderId: orderId,
            OrderRef: "ORD-42", SourceSystem: "Sap", Status: "Draft", Priority: "High",
            RequestedTransportMode: "Amr",
            RequestedBy: "alice", CreatedBy: "system", Notes: "rush",
            EarliestUtc: null, LatestUtc: null, SubmittedAt: null,
            RequiresDropPod: true, RequiresPickupPod: false,
            TotalItems: 2, TotalQuantity: 7, TotalWeightKg: 8.0,
            Items: new[] { item1, item2 });

        await projector.Consume(Ctx(evt));

        await store.Received(1).UpsertOnCreateAsync(
            orderId, "ORD-42", "Draft", "Sap", "High",
            "Amr",
            "alice", "system", "rush",
            totalItems: 2,
            totalQuantity: 7,
            totalWeightKg: 8.0,
            requiresDropPod: true, requiresPickupPod: false,
            Arg.Any<DateTime>(), Arg.Any<DateTime?>(),
            Arg.Any<DateTime?>(), Arg.Any<DateTime?>(),
            Arg.Is<string>(s => s.Contains("SKU-001") && s.Contains("SKU-002") && s.Contains("ORD-42")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Submitted_UpdatesStatus()
    {
        var (projector, store) = Build();
        var orderId = Guid.NewGuid();
        var evt = new DeliveryOrderSubmittedIntegrationEventV1(
            Guid.NewGuid(), DateTime.UtcNow, orderId);

        await projector.Consume(Ctx(evt));

        await store.Received(1).UpdateStatusAsync(
            orderId, "Submitted", evt.OccurredOn, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Validated_UpdatesStatus()
    {
        var (projector, store) = Build();
        var orderId = Guid.NewGuid();
        var evt = new DeliveryOrderValidatedIntegrationEventV1(
            Guid.NewGuid(), DateTime.UtcNow, orderId);

        await projector.Consume(Ctx(evt));

        await store.Received(1).UpdateStatusAsync(
            orderId, "Validated", evt.OccurredOn, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Confirmed_UpdatesStatusOnly()
    {
        var (projector, store) = Build();
        var orderId = Guid.NewGuid();
        var evt = new DeliveryOrderConfirmedIntegrationEventV1(
            Guid.NewGuid(), DateTime.UtcNow, orderId,
            "High", null, null, null, Array.Empty<ItemSummaryDto>());

        await projector.Consume(Ctx(evt));

        await store.Received(1).UpdateStatusAsync(
            orderId, "Confirmed", evt.OccurredOn, Arg.Any<CancellationToken>());
        await store.DidNotReceive().UpsertOnCreateAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string?>(),
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(),
            Arg.Any<int>(), Arg.Any<double>(), Arg.Any<double>(),
            Arg.Any<bool?>(), Arg.Any<bool?>(),
            Arg.Any<DateTime>(), Arg.Any<DateTime?>(),
            Arg.Any<DateTime?>(), Arg.Any<DateTime?>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Failed_UpdatesStatus()
    {
        var (projector, store) = Build();
        var orderId = Guid.NewGuid();
        var evt = new DeliveryOrderFailedIntegrationEventV1(
            Guid.NewGuid(), DateTime.UtcNow, orderId, "vendor rejected");

        await projector.Consume(Ctx(evt));

        await store.Received(1).UpdateStatusAsync(
            orderId, "Failed", evt.OccurredOn, Arg.Any<CancellationToken>());
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

        await store.DidNotReceive().UpdateStatusAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PermanentFailure_IsSwallowed()
    {
        var (projector, store) = Build();
        var evt = new DeliveryOrderFailedIntegrationEventV1(
            Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), "x");

        store.When(s => s.UpdateStatusAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()))
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

        store.When(s => s.UpdateStatusAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()))
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
            store, metrics, NullLogger<OrderListViewProjector>.Instance);
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
