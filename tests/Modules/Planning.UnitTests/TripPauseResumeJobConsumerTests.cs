using DTMS.Dispatch.IntegrationEvents;
using AMR.DeliveryPlanning.Planning.Application.Consumers;
using AMR.DeliveryPlanning.Planning.Domain.Entities;
using AMR.DeliveryPlanning.Planning.Domain.Enums;
using AMR.DeliveryPlanning.Planning.Domain.Repositories;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Planning.UnitTests;

public class TripPauseResumeJobConsumerTests
{
    [Fact]
    public async Task TripPaused_LinkedJob_FlipsToPausedAndPersists()
    {
        var (consumer, repo) = BuildPause();
        var tripId = Guid.NewGuid();
        var job = BuildExecutingJob(tripId);
        repo.GetByTripIdAsync(tripId, Arg.Any<CancellationToken>()).Returns(job);
        var evt = new TripPausedIntegrationEventV1(Guid.NewGuid(), DateTime.UtcNow, tripId);

        await consumer.Consume(Ctx(evt));

        job.Status.Should().Be(JobStatus.Paused);
        await repo.Received(1).UpdateAsync(job, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TripPaused_NoLinkedJob_LogsAndReturns()
    {
        var (consumer, repo) = BuildPause();
        var tripId = Guid.NewGuid();
        repo.GetByTripIdAsync(tripId, Arg.Any<CancellationToken>()).Returns((Job?)null);
        var evt = new TripPausedIntegrationEventV1(Guid.NewGuid(), DateTime.UtcNow, tripId);

        await consumer.Consume(Ctx(evt));

        await repo.DidNotReceive().UpdateAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TripResumed_PausedJob_FlipsToExecuting()
    {
        var (consumer, repo) = BuildResume();
        var tripId = Guid.NewGuid();
        var job = BuildExecutingJob(tripId);
        job.MarkPaused(tripId);
        repo.GetByTripIdAsync(tripId, Arg.Any<CancellationToken>()).Returns(job);
        var evt = new TripResumedIntegrationEventV1(Guid.NewGuid(), DateTime.UtcNow, tripId);

        await consumer.Consume(Ctx(evt));

        job.Status.Should().Be(JobStatus.Executing);
        await repo.Received(1).UpdateAsync(job, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TripResumed_NoLinkedJob_LogsAndReturns()
    {
        var (consumer, repo) = BuildResume();
        var tripId = Guid.NewGuid();
        repo.GetByTripIdAsync(tripId, Arg.Any<CancellationToken>()).Returns((Job?)null);
        var evt = new TripResumedIntegrationEventV1(Guid.NewGuid(), DateTime.UtcNow, tripId);

        await consumer.Consume(Ctx(evt));

        await repo.DidNotReceive().UpdateAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>());
    }

    private static (TripPausedJobConsumer consumer, IJobRepository repo) BuildPause()
    {
        var repo = Substitute.For<IJobRepository>();
        return (new TripPausedJobConsumer(repo, NullLogger<TripPausedJobConsumer>.Instance), repo);
    }

    private static (TripResumedJobConsumer consumer, IJobRepository repo) BuildResume()
    {
        var repo = Substitute.For<IJobRepository>();
        return (new TripResumedJobConsumer(repo, NullLogger<TripResumedJobConsumer>.Instance), repo);
    }

    private static Job BuildExecutingJob(Guid tripId)
    {
        var job = new Job(Guid.NewGuid(), "Normal");
        job.MarkDispatched(tripId, "K");
        job.MarkExecuting(tripId);
        return job;
    }

    private static ConsumeContext<T> Ctx<T>(T message) where T : class
    {
        var c = Substitute.For<ConsumeContext<T>>();
        c.Message.Returns(message);
        c.CancellationToken.Returns(CancellationToken.None);
        return c;
    }
}
