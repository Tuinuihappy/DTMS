using DTMS.Planning.Application.Projections;
using DTMS.Planning.IntegrationEvents;
using DTMS.SharedKernel.Projection;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Planning.UnitTests;

public class JobFactsProjectorTests
{
    [Fact]
    public async Task JobCreated_UpsertsRow()
    {
        var (projector, store) = Build();
        var jobId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var evt = new JobCreatedIntegrationEventV1(
            Guid.NewGuid(), DateTime.UtcNow, jobId, orderId);

        await projector.Consume(Ctx(evt));

        await store.Received(1).UpsertOnCreatedAsync(
            jobId, orderId, evt.OccurredOn, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task JobAssigned_SetsAssignedAtWithVehicle()
    {
        var (projector, store) = Build();
        var jobId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();
        var evt = new JobAssignedIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, jobId,
            DeliveryOrderId: Guid.NewGuid(),
            VehicleId: vehicleId,
            PickupStationId: Guid.NewGuid(),
            DropStationId: Guid.NewGuid());

        await projector.Consume(Ctx(evt));

        await store.Received(1).SetAssignedAtAsync(
            jobId, evt.OccurredOn, vehicleId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PlanCommitted_SetsCommittedAt()
    {
        var (projector, store) = Build();
        var jobId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();
        var evt = new PlanCommittedIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, jobId,
            DeliveryOrderId: Guid.NewGuid(), VehicleId: vehicleId,
            Legs: new List<PlannedLegDto>());

        await projector.Consume(Ctx(evt));

        await store.Received(1).SetCommittedAtAsync(
            jobId, evt.OccurredOn, vehicleId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task JobDispatched_SetsDispatchedWithAttemptAndVendorKey()
    {
        var (projector, store) = Build();
        var jobId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var evt = new JobDispatchedIntegrationEventV1(
            Guid.NewGuid(), DateTime.UtcNow, jobId,
            DeliveryOrderId: Guid.NewGuid(), TripId: tripId,
            VendorOrderKey: "VOK-1", AttemptNumber: 2);

        await projector.Consume(Ctx(evt));

        await store.Received(1).SetDispatchedAtAsync(
            jobId, evt.OccurredOn, tripId, "VOK-1", 2, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task JobExecuting_SetsExecutingAt()
    {
        var (projector, store) = Build();
        var jobId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var evt = new JobExecutingIntegrationEventV1(
            Guid.NewGuid(), DateTime.UtcNow, jobId,
            DeliveryOrderId: Guid.NewGuid(), TripId: tripId);

        await projector.Consume(Ctx(evt));

        await store.Received(1).SetExecutingAtAsync(
            jobId, evt.OccurredOn, tripId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task JobCompleted_SetsCompletedAt()
    {
        var (projector, store) = Build();
        var jobId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var evt = new JobCompletedIntegrationEventV1(
            Guid.NewGuid(), DateTime.UtcNow, jobId,
            DeliveryOrderId: Guid.NewGuid(), TripId: tripId);

        await projector.Consume(Ctx(evt));

        await store.Received(1).SetCompletedAtAsync(
            jobId, evt.OccurredOn, tripId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task JobFailed_SetsFailedWithReasonAndAttempt()
    {
        var (projector, store) = Build();
        var jobId = Guid.NewGuid();
        var evt = new JobFailedIntegrationEventV1(
            Guid.NewGuid(), DateTime.UtcNow, jobId,
            DeliveryOrderId: Guid.NewGuid(),
            Reason: "vendor 429", AttemptNumber: 3,
            FailureCategory: "VendorRateLimited");

        await projector.Consume(Ctx(evt));

        await store.Received(1).SetFailedAtAsync(
            jobId, evt.OccurredOn, "vendor 429", 3,
            "VendorRateLimited",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task JobCancelled_SetsCancelledWithReason()
    {
        var (projector, store) = Build();
        var jobId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var evt = new JobCancelledIntegrationEventV1(
            Guid.NewGuid(), DateTime.UtcNow, jobId,
            DeliveryOrderId: Guid.NewGuid(), TripId: tripId, Reason: "operator",
            FailureCategory: "OperatorCancelled");

        await projector.Consume(Ctx(evt));

        await store.Received(1).SetCancelledAtAsync(
            jobId, evt.OccurredOn, tripId, "operator",
            "OperatorCancelled",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task JobFailed_NullCategory_PassedThrough()
    {
        // Pre-V1.1 events have FailureCategory == null; projector must
        // pass null without throwing. Store collapses null to "None"
        // on write so the column never holds NULL.
        var (projector, store) = Build();
        var evt = new JobFailedIntegrationEventV1(
            Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(),
            DeliveryOrderId: Guid.NewGuid(), Reason: "legacy", AttemptNumber: 1,
            FailureCategory: null);

        await projector.Consume(Ctx(evt));

        await store.Received(1).SetFailedAtAsync(
            Arg.Any<Guid>(), Arg.Any<DateTime>(), "legacy", 1,
            null,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DuplicateEvent_IsSkipped()
    {
        var (projector, store) = Build();
        var evt = new JobFailedIntegrationEventV1(
            Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(),
            DeliveryOrderId: Guid.NewGuid(), Reason: "x", AttemptNumber: 1);

        store.HasProcessedEventAsync(
                JobFactsProjector.Name, evt.EventId, Arg.Any<CancellationToken>())
            .Returns(true);

        await projector.Consume(Ctx(evt));

        await store.DidNotReceive().SetFailedAtAsync(
            Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<string?>(),
            Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PermanentFailure_IsSwallowed()
    {
        var (projector, store) = Build();
        var evt = new JobFailedIntegrationEventV1(
            Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(),
            DeliveryOrderId: Guid.NewGuid(), Reason: "x", AttemptNumber: 1);

        store.When(s => s.SetFailedAtAsync(
                Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<string?>(),
                Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("schema drift"));

        var act = async () => await projector.Consume(Ctx(evt));
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task TransientFailure_IsRethrown()
    {
        var (projector, store) = Build();
        var evt = new JobFailedIntegrationEventV1(
            Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(),
            DeliveryOrderId: Guid.NewGuid(), Reason: "x", AttemptNumber: 1);

        store.When(s => s.SetFailedAtAsync(
                Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<string?>(),
                Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()))
            .Do(_ => throw new TimeoutException("db lock"));

        var act = async () => await projector.Consume(Ctx(evt));
        await act.Should().ThrowAsync<TimeoutException>();
    }

    private static (JobFactsProjector projector, IJobFactsProjectionStore store) Build()
    {
        var store = Substitute.For<IJobFactsProjectionStore>();
        store.HasProcessedEventAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(false);
        var metrics = new ProjectionMetrics();
        var projector = new JobFactsProjector(
            store, metrics, NullLogger<JobFactsProjector>.Instance);
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
