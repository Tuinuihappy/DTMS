using AMR.DeliveryPlanning.Planning.Application.Projections;
using AMR.DeliveryPlanning.Planning.IntegrationEvents;
using AMR.DeliveryPlanning.SharedKernel.Projection;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Planning.UnitTests;

public class JobStatusHistoryProjectorTests
{
    [Fact]
    public async Task FirstEvent_AppendsRowWithNullFromStatus()
    {
        var (projector, store) = Build();
        var jobId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var evt = new JobCreatedIntegrationEventV1(
            EventId: Guid.NewGuid(),
            OccurredOn: DateTime.UtcNow,
            JobId: jobId,
            DeliveryOrderId: orderId);

        await projector.Consume(Ctx(evt));

        await store.Received(1).AppendAsync(
            JobStatusHistoryProjector.Name,
            evt.EventId, jobId, orderId,
            fromStatus: null,
            toStatus: "Created",
            evt.OccurredOn,
            reason: null,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchedEvent_DerivesFromStatus()
    {
        var (projector, store) = Build();
        var jobId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var t0 = DateTime.UtcNow.AddMinutes(-5);
        store.GetLatestForJobAsync(jobId, Arg.Any<CancellationToken>())
            .Returns(("Committed", t0));

        var evt = new JobDispatchedIntegrationEventV1(
            Guid.NewGuid(), DateTime.UtcNow, jobId, orderId,
            TripId: Guid.NewGuid(), VendorOrderKey: "RIOT-123", AttemptNumber: 1);

        await projector.Consume(Ctx(evt));

        await store.Received(1).AppendAsync(
            JobStatusHistoryProjector.Name,
            evt.EventId, jobId, orderId,
            fromStatus: "Committed",
            toStatus: "Dispatched",
            evt.OccurredOn,
            reason: "vendor=RIOT-123 attempt=1",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FailedEvent_CarriesReasonAndAttempt()
    {
        var (projector, store) = Build();
        var jobId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var evt = new JobFailedIntegrationEventV1(
            Guid.NewGuid(), DateTime.UtcNow, jobId, orderId,
            Reason: "vendor 429", AttemptNumber: 2);

        await projector.Consume(Ctx(evt));

        await store.Received(1).AppendAsync(
            JobStatusHistoryProjector.Name,
            evt.EventId, jobId, orderId,
            fromStatus: null, toStatus: "Failed",
            evt.OccurredOn,
            reason: "attempt=2: vendor 429",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DuplicateEvent_IsSkipped()
    {
        var (projector, store) = Build();
        var evt = new JobCreatedIntegrationEventV1(
            Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), Guid.NewGuid());

        store.HasProcessedEventAsync(
                JobStatusHistoryProjector.Name, evt.EventId, Arg.Any<CancellationToken>())
            .Returns(true);

        await projector.Consume(Ctx(evt));

        await store.DidNotReceive().AppendAsync(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(),
            Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<DateTime>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OutOfOrderEvent_IsSkipped()
    {
        var (projector, store) = Build();
        var jobId = Guid.NewGuid();
        var latestTime = DateTime.UtcNow;
        store.GetLatestForJobAsync(jobId, Arg.Any<CancellationToken>())
            .Returns(("Dispatched", latestTime));

        var evt = new JobCreatedIntegrationEventV1(
            Guid.NewGuid(), latestTime.AddMinutes(-1), jobId, Guid.NewGuid());

        await projector.Consume(Ctx(evt));

        await store.DidNotReceive().AppendAsync(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(),
            Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<DateTime>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PermanentFailure_IsSwallowed()
    {
        var (projector, store) = Build();
        var evt = new JobCreatedIntegrationEventV1(
            Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), Guid.NewGuid());

        store.When(s => s.AppendAsync(
                Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(),
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
        var evt = new JobCreatedIntegrationEventV1(
            Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), Guid.NewGuid());

        store.When(s => s.AppendAsync(
                Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(),
                Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<DateTime>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>()))
            .Do(_ => throw new TimeoutException("db lock"));

        var act = async () => await projector.Consume(Ctx(evt));
        await act.Should().ThrowAsync<TimeoutException>();
    }

    private static (JobStatusHistoryProjector projector, IJobStatusHistoryProjectionStore store) Build()
    {
        var store = Substitute.For<IJobStatusHistoryProjectionStore>();
        store.HasProcessedEventAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(false);
        store.GetLatestForJobAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(((string, DateTime)?)null);
        var metrics = new ProjectionMetrics();
        var projector = new JobStatusHistoryProjector(
            store, metrics, NullLogger<JobStatusHistoryProjector>.Instance);
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
