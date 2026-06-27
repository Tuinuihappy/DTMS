using DTMS.Planning.Application.Commands.MarkJobDispatched;
using DTMS.Planning.Domain.Entities;
using DTMS.Planning.Domain.Enums;
using DTMS.Planning.Domain.Repositories;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Planning.UnitTests;

// T1.5 — idempotency guard on MarkJobDispatched. When MassTransit redelivers
// the auto-planning consumer (T1.1) the dispatch step may run twice. The
// second run must succeed silently if the prior attempt already bound the
// Job to the same TripId, and must fail loudly if the TripId differs (a
// signal that something is genuinely diverging — never paper over).
public class T1_MarkJobDispatchedIdempotencyTests
{
    private static readonly Guid OrderId = Guid.NewGuid();
    private static readonly Guid TripId = Guid.NewGuid();
    private const string VendorKey = "VK-1";

    [Fact]
    public async Task Handle_JobCreated_MarksDispatched()
    {
        var (handler, repo) = Build();
        var job = NewCreatedJob();
        repo.GetByIdAsync(job.Id, Arg.Any<CancellationToken>()).Returns(job);

        var result = await handler.Handle(
            new MarkJobDispatchedCommand(job.Id, TripId, VendorKey),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        job.Status.Should().Be(JobStatus.Dispatched);
        job.TripId.Should().Be(TripId);
        job.VendorOrderKey.Should().Be(VendorKey);
        await repo.Received(1).UpdateAsync(job, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AlreadyDispatchedSameTripId_ReturnsSuccessWithoutMutation()
    {
        // Redelivered message: Job was already marked Dispatched on the prior
        // run with the same TripId. The handler must short-circuit — no
        // domain mutation, no repo update — so the second attempt is a true
        // no-op rather than re-raising the domain event.
        var (handler, repo) = Build();
        var job = NewCreatedJob();
        job.MarkDispatched(TripId, VendorKey);
        job.ClearDomainEvents();
        repo.GetByIdAsync(job.Id, Arg.Any<CancellationToken>()).Returns(job);

        var result = await handler.Handle(
            new MarkJobDispatchedCommand(job.Id, TripId, VendorKey),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        job.DomainEvents.Should().BeEmpty("no domain event on the no-op path");
        await repo.DidNotReceive().UpdateAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AlreadyDispatchedDifferentTripId_FailsLoudly()
    {
        // Divergence: the same Job was already bound to a DIFFERENT trip on
        // the prior attempt. Returning success would orphan one of the
        // vendor orders silently. Fail with a message ops can grep so the
        // discrepancy is investigated.
        var (handler, repo) = Build();
        var existingTrip = Guid.NewGuid();
        var attemptedTrip = Guid.NewGuid();
        var job = NewCreatedJob();
        job.MarkDispatched(existingTrip, VendorKey);
        repo.GetByIdAsync(job.Id, Arg.Any<CancellationToken>()).Returns(job);

        var result = await handler.Handle(
            new MarkJobDispatchedCommand(job.Id, attemptedTrip, VendorKey),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("already dispatched");
        await repo.DidNotReceive().UpdateAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_JobNotFound_ReturnsFailure()
    {
        var (handler, repo) = Build();
        repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Job?)null);

        var result = await handler.Handle(
            new MarkJobDispatchedCommand(Guid.NewGuid(), TripId, VendorKey),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    private static (MarkJobDispatchedCommandHandler handler, IJobRepository repo) Build()
    {
        var repo = Substitute.For<IJobRepository>();
        var handler = new MarkJobDispatchedCommandHandler(
            repo, NullLogger<MarkJobDispatchedCommandHandler>.Instance);
        return (handler, repo);
    }

    private static Job NewCreatedJob()
    {
        var job = new Job(OrderId, "Normal");
        job.SetEnvelopeAnchor(1, Guid.NewGuid(), Guid.NewGuid());
        return job;
    }
}
