using AMR.DeliveryPlanning.Planning.Application.Commands.CreateJobAnchor;
using AMR.DeliveryPlanning.Planning.Domain.Entities;
using AMR.DeliveryPlanning.Planning.Domain.Repositories;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Planning.UnitTests;

// T1.5 — idempotency guard on CreateJobAnchor. The guard is what makes
// MassTransit retries (T1.1) safe for the auto-planning consumer: a redelivered
// message re-runs the consumer, but the second CreateJobAnchor returns the
// existing Job rather than creating a duplicate row.
public class T1_CreateJobAnchorIdempotencyTests
{
    private static readonly Guid OrderId = Guid.NewGuid();
    private static readonly Guid PickupStationId = Guid.NewGuid();
    private static readonly Guid DropStationId = Guid.NewGuid();

    [Fact]
    public async Task Handle_NoExistingJob_CreatesNewAnchor()
    {
        var (handler, repo) = Build();
        repo.GetByDeliveryOrderIdAsync(OrderId, Arg.Any<CancellationToken>())
            .Returns(new List<Job>());

        var result = await handler.Handle(NewCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await repo.Received(1).AddAsync(
            Arg.Is<Job>(j => j.DeliveryOrderId == OrderId && j.GroupIndex == 1),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ExistingJobSameGroup_ReturnsExistingWithoutAdd()
    {
        var existing = BuildAnchorJob(groupIndex: 1);
        var (handler, repo) = Build();
        repo.GetByDeliveryOrderIdAsync(OrderId, Arg.Any<CancellationToken>())
            .Returns(new List<Job> { existing });

        var result = await handler.Handle(NewCommand(groupIndex: 1), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(existing.Id, "idempotent path returns the prior Job's id");
        await repo.DidNotReceive().AddAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ExistingJobDifferentGroup_CreatesNewForThisGroup()
    {
        var differentGroup = BuildAnchorJob(groupIndex: 1);
        var (handler, repo) = Build();
        repo.GetByDeliveryOrderIdAsync(OrderId, Arg.Any<CancellationToken>())
            .Returns(new List<Job> { differentGroup });

        var result = await handler.Handle(NewCommand(groupIndex: 2), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBe(differentGroup.Id, "group 2 is a distinct anchor");
        await repo.Received(1).AddAsync(
            Arg.Is<Job>(j => j.GroupIndex == 2),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AddRaceWithConcurrentInsert_ReturnsRacersId()
    {
        // First lookup: empty (we think we can add). AddAsync throws (unique
        // violation from a concurrent insert). Second lookup: now sees the
        // racer. Handler must recover and return the racer's id.
        var (handler, repo) = Build();
        var raceWinner = BuildAnchorJob(groupIndex: 1);

        repo.GetByDeliveryOrderIdAsync(OrderId, Arg.Any<CancellationToken>())
            .Returns(
                _ => new List<Job>(),                // before AddAsync
                _ => new List<Job> { raceWinner });  // after the catch

        repo.AddAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("simulated unique-violation"));

        var result = await handler.Handle(NewCommand(groupIndex: 1), CancellationToken.None);

        result.IsSuccess.Should().BeTrue("the race resolves to the existing anchor");
        result.Value.Should().Be(raceWinner.Id);
    }

    [Fact]
    public async Task Handle_AddFailsWithNoConcurrentInsert_ReturnsFailure()
    {
        // AddAsync throws and a follow-up lookup still finds nothing — that's
        // a genuine persistence failure, not a race. Handler must propagate
        // the failure so MassTransit retries the whole consumer.
        var (handler, repo) = Build();
        repo.GetByDeliveryOrderIdAsync(OrderId, Arg.Any<CancellationToken>())
            .Returns(new List<Job>());
        repo.AddAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("DB connection lost"));

        var result = await handler.Handle(NewCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("DB connection lost");
    }

    private static (CreateJobAnchorCommandHandler handler, IJobRepository repo) Build()
    {
        var repo = Substitute.For<IJobRepository>();
        var handler = new CreateJobAnchorCommandHandler(
            repo, NullLogger<CreateJobAnchorCommandHandler>.Instance);
        return (handler, repo);
    }

    private static CreateJobAnchorCommand NewCommand(int groupIndex = 1) =>
        new(
            DeliveryOrderId: OrderId,
            GroupIndex: groupIndex,
            PickupStationId: PickupStationId,
            DropStationId: DropStationId,
            Priority: "Normal",
            RequestedTransportMode: null,
            SlaDeadline: null);

    private static Job BuildAnchorJob(int groupIndex)
    {
        var job = new Job(OrderId, "Normal");
        job.SetEnvelopeAnchor(groupIndex, PickupStationId, DropStationId);
        return job;
    }
}
