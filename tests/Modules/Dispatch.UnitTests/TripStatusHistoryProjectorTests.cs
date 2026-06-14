using AMR.DeliveryPlanning.Dispatch.Application.Projections;
using AMR.DeliveryPlanning.Dispatch.IntegrationEvents;
using AMR.DeliveryPlanning.SharedKernel.Projection;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Dispatch.UnitTests;

public class TripStatusHistoryProjectorTests
{
    [Fact]
    public async Task TripStarted_AppendsInProgressWithNullFromStatus()
    {
        var (projector, store) = Build();
        var tripId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();
        var evt = new TripStartedIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, tripId,
            JobId: Guid.Empty, VehicleId: vehicleId, DeliveryOrderId: orderId);

        await projector.Consume(Ctx(evt));

        await store.Received(1).AppendAsync(
            TripStatusHistoryProjector.Name,
            evt.EventId, tripId,
            deliveryOrderId: orderId,
            jobId: (Guid?)null,
            fromStatus: null,
            toStatus: "InProgress",
            evt.OccurredOn,
            reason: $"vehicle={vehicleId}",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TripPaused_CarriesForwardOrderIdFromLatest()
    {
        var (projector, store) = Build();
        var tripId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var t0 = DateTime.UtcNow.AddMinutes(-5);
        store.GetLatestForTripAsync(tripId, Arg.Any<CancellationToken>())
            .Returns(new TripHistoryLatest("InProgress", t0, orderId, jobId));

        var evt = new TripPausedIntegrationEventV1(
            Guid.NewGuid(), DateTime.UtcNow, tripId);

        await projector.Consume(Ctx(evt));

        await store.Received(1).AppendAsync(
            TripStatusHistoryProjector.Name,
            evt.EventId, tripId,
            deliveryOrderId: orderId,
            jobId: jobId,
            fromStatus: "InProgress",
            toStatus: "Paused",
            evt.OccurredOn,
            reason: null,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TripResumed_FlipsBackToInProgress()
    {
        var (projector, store) = Build();
        var tripId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var t0 = DateTime.UtcNow.AddMinutes(-2);
        store.GetLatestForTripAsync(tripId, Arg.Any<CancellationToken>())
            .Returns(new TripHistoryLatest("Paused", t0, orderId, null));

        var evt = new TripResumedIntegrationEventV1(
            Guid.NewGuid(), DateTime.UtcNow, tripId);

        await projector.Consume(Ctx(evt));

        await store.Received(1).AppendAsync(
            TripStatusHistoryProjector.Name,
            evt.EventId, tripId,
            deliveryOrderId: orderId,
            jobId: (Guid?)null,
            fromStatus: "Paused",
            toStatus: "InProgress",
            evt.OccurredOn,
            reason: "Resumed from pause",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TripFailed_CarriesReason()
    {
        var (projector, store) = Build();
        var tripId = Guid.NewGuid();
        var evt = new TripFailedIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, tripId,
            JobId: Guid.NewGuid(), DeliveryOrderId: Guid.NewGuid(),
            Reason: "vendor rejected", VendorUpperKey: "UK-1");

        await projector.Consume(Ctx(evt));

        await store.Received(1).AppendAsync(
            TripStatusHistoryProjector.Name,
            evt.EventId, tripId,
            Arg.Any<Guid?>(), Arg.Any<Guid?>(),
            fromStatus: null, toStatus: "Failed",
            evt.OccurredOn, reason: "vendor rejected",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DuplicateEvent_IsSkipped()
    {
        var (projector, store) = Build();
        var evt = new TripStartedIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(),
            Guid.Empty, Guid.NewGuid(), Guid.NewGuid());

        store.HasProcessedEventAsync(
                TripStatusHistoryProjector.Name, evt.EventId, Arg.Any<CancellationToken>())
            .Returns(true);

        await projector.Consume(Ctx(evt));

        await store.DidNotReceive().AppendAsync(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(),
            Arg.Any<Guid?>(), Arg.Any<Guid?>(),
            Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<DateTime>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OutOfOrderEvent_IsSkipped()
    {
        var (projector, store) = Build();
        var tripId = Guid.NewGuid();
        var latestTime = DateTime.UtcNow;
        store.GetLatestForTripAsync(tripId, Arg.Any<CancellationToken>())
            .Returns(new TripHistoryLatest("Completed", latestTime, null, null));

        var evt = new TripStartedIntegrationEvent(
            Guid.NewGuid(), latestTime.AddMinutes(-1), tripId,
            Guid.Empty, Guid.NewGuid(), Guid.NewGuid());

        await projector.Consume(Ctx(evt));

        await store.DidNotReceive().AppendAsync(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(),
            Arg.Any<Guid?>(), Arg.Any<Guid?>(),
            Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<DateTime>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PermanentFailure_IsSwallowed()
    {
        var (projector, store) = Build();
        var evt = new TripCancelledIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), "abort", null);

        store.When(s => s.AppendAsync(
                Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(),
                Arg.Any<Guid?>(), Arg.Any<Guid?>(),
                Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<DateTime>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("schema drift"));

        var act = async () => await projector.Consume(Ctx(evt));
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task TransientFailure_IsRethrown()
    {
        var (projector, store) = Build();
        var evt = new TripCancelledIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), "abort", null);

        store.When(s => s.AppendAsync(
                Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(),
                Arg.Any<Guid?>(), Arg.Any<Guid?>(),
                Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<DateTime>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>()))
            .Do(_ => throw new TimeoutException("db lock"));

        var act = async () => await projector.Consume(Ctx(evt));
        await act.Should().ThrowAsync<TimeoutException>();
    }

    private static (TripStatusHistoryProjector projector, ITripStatusHistoryProjectionStore store) Build()
    {
        var store = Substitute.For<ITripStatusHistoryProjectionStore>();
        store.HasProcessedEventAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(false);
        store.GetLatestForTripAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((TripHistoryLatest?)null);
        var metrics = new ProjectionMetrics();
        var projector = new TripStatusHistoryProjector(
            store, metrics, new NoopTripRealtimePublisher(), NullLogger<TripStatusHistoryProjector>.Instance);
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
