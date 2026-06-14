using AMR.DeliveryPlanning.Dispatch.Application.Projections;
using AMR.DeliveryPlanning.Dispatch.IntegrationEvents;
using AMR.DeliveryPlanning.SharedKernel.Projection;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Dispatch.UnitTests;

public class TripFactsProjectorTests
{
    [Fact]
    public async Task TripStarted_SetsStartedAtAndIds()
    {
        var (projector, store) = Build();
        var tripId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();
        var evt = new TripStartedIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, tripId, jobId, vehicleId, orderId,
            VendorVehicleKey: "ROBOT-DELTA-42");

        await projector.Consume(Ctx(evt));

        await store.Received(1).SetStartedAtAsync(
            tripId, evt.OccurredOn, orderId, jobId, vehicleId,
            "ROBOT-DELTA-42",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TripStarted_NormalizesEmptyGuidsToNull()
    {
        var (projector, store) = Build();
        var tripId = Guid.NewGuid();
        var evt = new TripStartedIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, tripId,
            JobId: Guid.Empty, VehicleId: Guid.Empty, DeliveryOrderId: Guid.Empty);

        await projector.Consume(Ctx(evt));

        await store.Received(1).SetStartedAtAsync(
            tripId, evt.OccurredOn, null, null, null,
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TripStarted_NullVendorVehicleKey_StillPassedThrough()
    {
        // Pre-V1.1 events have VendorVehicleKey == null. Projector must
        // pass null without throwing — store's first-write-wins rule
        // prevents overwriting a previously-captured key.
        var (projector, store) = Build();
        var tripId = Guid.NewGuid();
        var evt = new TripStartedIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, tripId,
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            VendorVehicleKey: null);

        await projector.Consume(Ctx(evt));

        await store.Received(1).SetStartedAtAsync(
            tripId, evt.OccurredOn,
            Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<Guid?>(),
            null,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TripPaused_RecordsPause()
    {
        var (projector, store) = Build();
        var tripId = Guid.NewGuid();
        var evt = new TripPausedIntegrationEventV1(Guid.NewGuid(), DateTime.UtcNow, tripId);

        await projector.Consume(Ctx(evt));

        await store.Received(1).RecordPausedAsync(
            tripId, evt.OccurredOn, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TripResumed_RecordsResume()
    {
        var (projector, store) = Build();
        var tripId = Guid.NewGuid();
        var evt = new TripResumedIntegrationEventV1(Guid.NewGuid(), DateTime.UtcNow, tripId);

        await projector.Consume(Ctx(evt));

        await store.Received(1).RecordResumedAsync(
            tripId, evt.OccurredOn, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TripCompleted_SetsCompletedWithVendorKey()
    {
        var (projector, store) = Build();
        var tripId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var evt = new TripCompletedIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, tripId,
            JobId: Guid.NewGuid(), DeliveryOrderId: orderId, VendorUpperKey: "VENDOR-A");

        await projector.Consume(Ctx(evt));

        await store.Received(1).SetCompletedAtAsync(
            tripId, evt.OccurredOn, orderId, Arg.Any<Guid?>(), "VENDOR-A",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TripFailed_SetsFailedWithReason()
    {
        var (projector, store) = Build();
        var tripId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var evt = new TripFailedIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, tripId,
            JobId: Guid.NewGuid(), DeliveryOrderId: orderId,
            Reason: "robot stuck", VendorUpperKey: "VENDOR-A");

        await projector.Consume(Ctx(evt));

        await store.Received(1).SetFailedAtAsync(
            tripId, evt.OccurredOn, orderId, Arg.Any<Guid?>(),
            "VENDOR-A", "robot stuck", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TripCancelled_SetsCancelledWithReason()
    {
        var (projector, store) = Build();
        var tripId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var evt = new TripCancelledIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, tripId,
            JobId: Guid.NewGuid(), DeliveryOrderId: orderId,
            Reason: "operator", VendorUpperKey: null);

        await projector.Consume(Ctx(evt));

        await store.Received(1).SetCancelledAtAsync(
            tripId, evt.OccurredOn, orderId, Arg.Any<Guid?>(),
            null, "operator", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DuplicateEvent_IsSkipped()
    {
        var (projector, store) = Build();
        var evt = new TripCompletedIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), "VK");

        store.HasProcessedEventAsync(
                TripFactsProjector.Name, evt.EventId, Arg.Any<CancellationToken>())
            .Returns(true);

        await projector.Consume(Ctx(evt));

        await store.DidNotReceive().SetCompletedAtAsync(
            Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<Guid?>(),
            Arg.Any<Guid?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PermanentFailure_IsSwallowed()
    {
        var (projector, store) = Build();
        var evt = new TripCompletedIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), "VK");

        store.When(s => s.SetCompletedAtAsync(
                Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<Guid?>(),
                Arg.Any<Guid?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()))
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
            Guid.NewGuid(), Guid.NewGuid(), "VK");

        store.When(s => s.SetCompletedAtAsync(
                Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<Guid?>(),
                Arg.Any<Guid?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()))
            .Do(_ => throw new TimeoutException("db lock"));

        var act = async () => await projector.Consume(Ctx(evt));
        await act.Should().ThrowAsync<TimeoutException>();
    }

    private static (TripFactsProjector projector, ITripFactsProjectionStore store) Build()
    {
        var store = Substitute.For<ITripFactsProjectionStore>();
        store.HasProcessedEventAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(false);
        var metrics = new ProjectionMetrics();
        var projector = new TripFactsProjector(
            store, metrics, NullLogger<TripFactsProjector>.Instance);
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
