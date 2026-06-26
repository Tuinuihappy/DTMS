using AMR.DeliveryPlanning.DeliveryOrder.Application.Projections;
using DTMS.DeliveryOrder.IntegrationEvents;
using DTMS.Dispatch.IntegrationEvents;
using DTMS.SharedKernel.Projection;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DeliveryOrder.UnitTests;

public class OrderActivityProjectorTests
{
    [Fact]
    public async Task OrderConfirmed_AppendsRowWithLifecycleCategory()
    {
        var (projector, store) = Build();
        var orderId = Guid.NewGuid();
        var evt = new DeliveryOrderConfirmedIntegrationEventV1(
            Guid.NewGuid(), DateTime.UtcNow, orderId,
            "Normal", null, null, null, Array.Empty<ItemSummaryDto>());

        await projector.Consume(Ctx(evt));

        await store.Received(1).AppendAsync(
            OrderActivityProjector.Name,
            evt.EventId, orderId,
            category: "OrderLifecycle",
            eventType: "OrderConfirmed",
            details: null,
            actorId: null,
            evt.OccurredOn,
            relatedTripId: (Guid?)null,
            attemptNumber: (int?)null,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OrderAmended_AppendsRowWithAmendmentCategory()
    {
        var (projector, store) = Build();
        var orderId = Guid.NewGuid();
        var evt = new DeliveryOrderAmendedIntegrationEventV1(
            Guid.NewGuid(), DateTime.UtcNow, orderId, "service window changed");

        await projector.Consume(Ctx(evt));

        await store.Received(1).AppendAsync(
            OrderActivityProjector.Name,
            evt.EventId, orderId,
            category: "Amendment",
            eventType: "OrderAmended",
            details: "service window changed",
            actorId: null,
            evt.OccurredOn,
            relatedTripId: (Guid?)null,
            attemptNumber: (int?)null,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TripStarted_AppendsRowWithTripCategoryAndTripId()
    {
        var (projector, store) = Build();
        var orderId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();
        var evt = new TripStartedIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, tripId,
            JobId: Guid.Empty, VehicleId: vehicleId, DeliveryOrderId: orderId);

        await projector.Consume(Ctx(evt));

        await store.Received(1).AppendAsync(
            OrderActivityProjector.Name,
            evt.EventId, orderId,
            category: "TripExecution",
            eventType: "TripStarted",
            details: $"vehicle {vehicleId}",
            actorId: null,
            evt.OccurredOn,
            relatedTripId: tripId,
            attemptNumber: (int?)null,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TripFailed_CarriesReason()
    {
        var (projector, store) = Build();
        var orderId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var evt = new TripFailedIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, tripId,
            JobId: Guid.NewGuid(), DeliveryOrderId: orderId,
            Reason: "vendor rejected", VendorUpperKey: "UK-1");

        await projector.Consume(Ctx(evt));

        await store.Received(1).AppendAsync(
            OrderActivityProjector.Name,
            evt.EventId, orderId,
            category: "TripExecution",
            eventType: "TripFailed",
            details: "vendor rejected",
            actorId: null,
            evt.OccurredOn,
            relatedTripId: tripId,
            attemptNumber: (int?)null,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DuplicateEvent_IsSkipped()
    {
        var (projector, store) = Build();
        var evt = new DeliveryOrderConfirmedIntegrationEventV1(
            Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(),
            "Normal", null, null, null, Array.Empty<ItemSummaryDto>());

        store.HasProcessedEventAsync(
                OrderActivityProjector.Name, evt.EventId, Arg.Any<CancellationToken>())
            .Returns(true);

        await projector.Consume(Ctx(evt));

        await store.DidNotReceive().AppendAsync(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(),
            Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string?>(), Arg.Any<string?>(),
            Arg.Any<DateTime>(),
            Arg.Any<Guid?>(), Arg.Any<int?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EmptyOrderId_IsSkipped()
    {
        var (projector, store) = Build();
        var evt = new DeliveryOrderConfirmedIntegrationEventV1(
            Guid.NewGuid(), DateTime.UtcNow, Guid.Empty,
            "Normal", null, null, null, Array.Empty<ItemSummaryDto>());

        await projector.Consume(Ctx(evt));

        await store.DidNotReceive().AppendAsync(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(),
            Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string?>(), Arg.Any<string?>(),
            Arg.Any<DateTime>(),
            Arg.Any<Guid?>(), Arg.Any<int?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TripPaused_IsSkipped_NoDeliveryOrderInPayload()
    {
        var (projector, store) = Build();
        var evt = new TripPausedIntegrationEventV1(
            Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid());

        await projector.Consume(Ctx(evt));

        await store.DidNotReceive().AppendAsync(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(),
            Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string?>(), Arg.Any<string?>(),
            Arg.Any<DateTime>(),
            Arg.Any<Guid?>(), Arg.Any<int?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PermanentFailure_IsSwallowed()
    {
        var (projector, store) = Build();
        var evt = new DeliveryOrderConfirmedIntegrationEventV1(
            Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(),
            "Normal", null, null, null, Array.Empty<ItemSummaryDto>());

        store.When(s => s.AppendAsync(
                Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(),
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<DateTime>(),
                Arg.Any<Guid?>(), Arg.Any<int?>(),
                Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("schema drift"));

        var act = async () => await projector.Consume(Ctx(evt));
        await act.Should().NotThrowAsync();
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
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<DateTime>(),
                Arg.Any<Guid?>(), Arg.Any<int?>(),
                Arg.Any<CancellationToken>()))
            .Do(_ => throw new TimeoutException("db lock"));

        var act = async () => await projector.Consume(Ctx(evt));
        await act.Should().ThrowAsync<TimeoutException>();
    }

    private static (OrderActivityProjector projector, IOrderActivityProjectionStore store) Build()
    {
        var store = Substitute.For<IOrderActivityProjectionStore>();
        store.HasProcessedEventAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(false);
        var metrics = new ProjectionMetrics();
        var projector = new OrderActivityProjector(
            store, metrics, new NoopOrderRealtimePublisher(), NullLogger<OrderActivityProjector>.Instance);
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
