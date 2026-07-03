using DTMS.Dispatch.Domain.Entities;
using DTMS.Dispatch.Domain.Enums;
using DTMS.Dispatch.Domain.Repositories;
using DTMS.Dispatch.Domain.Services;
using DTMS.Dispatch.IntegrationEvents;
using DTMS.Transport.Manual.Application.Commands.AcknowledgeTrip;
using DTMS.Transport.Manual.Application.Services;
using DTMS.Transport.Manual.Domain.Entities;
using DTMS.Transport.Manual.Domain.Enums;
using DTMS.Transport.Manual.Domain.Repositories;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Transport.Manual.UnitTests;

// WMS PR-4b (PR-F) — Tests for both legacy (auto-assigned trip → operator
// acknowledges) and pool (unassigned trip → operator claims + starts in
// one atomic action) paths of AcknowledgeTripCommandHandler.
public class AcknowledgeTripHandlerTests
{
    private readonly IManualTripExtensionRepository _extensions = Substitute.For<IManualTripExtensionRepository>();
    private readonly ITripRepository _trips = Substitute.For<ITripRepository>();
    private readonly IOperatorRepository _operators = Substitute.For<IOperatorRepository>();
    private readonly ITripItemSnapshotProvider _snapshots = Substitute.For<ITripItemSnapshotProvider>();
    private readonly IOperatorPoolBroadcaster _broadcaster = Substitute.For<IOperatorPoolBroadcaster>();
    private readonly IPoolMetricsSink _metrics = Substitute.For<IPoolMetricsSink>();
    private readonly ManualDispatchOptions _options = new();

    private AcknowledgeTripCommandHandler CreateSut()
    {
        _snapshots.GetForTripAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                  .Returns(Array.Empty<TripItemSnapshot>());
        return new(
            _extensions, _trips, _operators, _snapshots, _broadcaster, _metrics,
            Options.Create(_options),
            NullLogger<AcknowledgeTripCommandHandler>.Instance);
    }

    // ── Post-claim path — extension exists because trip was already claimed ──

    [Fact]
    public async Task Handle_PostClaim_DifferentOperator_ReturnsAlreadyClaimed()
    {
        // The trip was already claimed via pool CAS by someone else and
        // an extension row exists. A second operator taps ack — return
        // the same 409-sentinel the CAS-race loser would get.
        var tripId = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        var ext = ManualTripExtension.AssignToOperator(tripId, owner, null, null);
        _extensions.GetByTripIdAsync(tripId, Arg.Any<CancellationToken>()).Returns(ext);
        var sut = CreateSut();

        var result = await sut.Handle(new AcknowledgeTripCommand(tripId, other), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(AcknowledgeTripErrorCodes.AlreadyClaimed);
    }

    [Fact]
    public async Task Handle_PostClaim_SameOperatorRetap_ReturnsSuccess_WithoutMutation()
    {
        // Winner retries the ack (flaky network, background sync). The
        // trip + extension are already in the post-claim state; nothing
        // to mutate. No trip reload, no broadcast, no CAS.
        var owner = Guid.NewGuid();
        var trip = Trip.CreateForEnvelope(
            deliveryOrderId: Guid.NewGuid(), upperKey: "UK-DUP-1", vendorOrderKey: null);
        var ext = ManualTripExtension.AssignToOperator(trip.Id, owner, null, null);
        ext.MarkAcknowledged();
        _extensions.GetByTripIdAsync(trip.Id, Arg.Any<CancellationToken>()).Returns(ext);
        var sut = CreateSut();

        var result = await sut.Handle(new AcknowledgeTripCommand(trip.Id, owner), default);

        result.IsSuccess.Should().BeTrue();
        await _trips.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _trips.DidNotReceive().UpdateAsync(Arg.Any<Trip>(), Arg.Any<CancellationToken>());
        await _trips.DidNotReceive().TryClaimFromPoolAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _broadcaster.DidNotReceive().BroadcastClaimedAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    // ── Pool path — no extension, trip dispatched to pool ────────────

    [Fact]
    public async Task Handle_Pool_HappyPath_CasWins_TransitionsTrip_BroadcastsClaimed()
    {
        var op = MakeOperator("EMP-001", "Alice");
        var trip = PooledTrip();
        _extensions.GetByTripIdAsync(trip.Id, Arg.Any<CancellationToken>()).Returns((ManualTripExtension?)null);
        _operators.GetByIdAsync(op.Id, Arg.Any<CancellationToken>()).Returns(op);
        _trips.GetByIdAsync(trip.Id, Arg.Any<CancellationToken>()).Returns(trip);
        _trips.TryClaimFromPoolAsync(trip.Id, op.Id, Arg.Any<CancellationToken>()).Returns(true);

        var sut = CreateSut();
        var result = await sut.Handle(new AcknowledgeTripCommand(trip.Id, op.Id), default);

        result.IsSuccess.Should().BeTrue();
        trip.Status.Should().Be(TripStatus.InProgress);
        await _trips.Received(1).TryClaimFromPoolAsync(trip.Id, op.Id, Arg.Any<CancellationToken>());
        await _extensions.Received(1).AddAsync(
            Arg.Is<ManualTripExtension>(e => e.TripId == trip.Id && e.OperatorId == op.Id && e.AcknowledgedAt.HasValue),
            Arg.Any<CancellationToken>());
        await _broadcaster.Received(1).BroadcastClaimedAsync(
            trip.Id, op.Id, op.DisplayName, Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Pool_CasLost_ReturnsAlreadyClaimedError_NoBroadcast()
    {
        var op = MakeOperator("EMP-002", "Bob");
        var trip = PooledTrip();
        _extensions.GetByTripIdAsync(trip.Id, Arg.Any<CancellationToken>()).Returns((ManualTripExtension?)null);
        _operators.GetByIdAsync(op.Id, Arg.Any<CancellationToken>()).Returns(op);
        _trips.GetByIdAsync(trip.Id, Arg.Any<CancellationToken>()).Returns(trip);
        // Another operator won the CAS between our pre-check and our own UPDATE.
        _trips.TryClaimFromPoolAsync(trip.Id, op.Id, Arg.Any<CancellationToken>()).Returns(false);

        var sut = CreateSut();
        var result = await sut.Handle(new AcknowledgeTripCommand(trip.Id, op.Id), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(AcknowledgeTripErrorCodes.AlreadyClaimed);
        await _trips.DidNotReceive().UpdateAsync(Arg.Any<Trip>(), Arg.Any<CancellationToken>());
        await _extensions.DidNotReceive().AddAsync(Arg.Any<ManualTripExtension>(), Arg.Any<CancellationToken>());
        await _broadcaster.DidNotReceive().BroadcastClaimedAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Pool_TripNeverDispatched_AmrLike_Rejects()
    {
        // AMR trips never enter the pool — DispatchedAt stays null. Guard
        // catches this before the CAS so we return a clearer error than 409.
        var op = MakeOperator("EMP-003", "Carol");
        var amrTrip = Trip.CreateForEnvelope(
            deliveryOrderId: Guid.NewGuid(), upperKey: "UK-AMR", vendorOrderKey: "RIOT-KEY-1");
        _extensions.GetByTripIdAsync(amrTrip.Id, Arg.Any<CancellationToken>()).Returns((ManualTripExtension?)null);
        _operators.GetByIdAsync(op.Id, Arg.Any<CancellationToken>()).Returns(op);
        _trips.GetByIdAsync(amrTrip.Id, Arg.Any<CancellationToken>()).Returns(amrTrip);

        var sut = CreateSut();
        var result = await sut.Handle(new AcknowledgeTripCommand(amrTrip.Id, op.Id), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("did not come through the pool");
        await _trips.DidNotReceive().TryClaimFromPoolAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Pool_SameOperatorRetap_Idempotent_ReturnsSuccessWithoutSecondClaim()
    {
        // Winner's phone retries the ack on flaky network — the trip is
        // already ClaimedByOperatorId=me. Return 204 quietly instead of 409.
        var op = MakeOperator("EMP-004", "Dan");
        var trip = ClaimedTrip(op.Id);
        _extensions.GetByTripIdAsync(trip.Id, Arg.Any<CancellationToken>()).Returns((ManualTripExtension?)null);
        _operators.GetByIdAsync(op.Id, Arg.Any<CancellationToken>()).Returns(op);
        _trips.GetByIdAsync(trip.Id, Arg.Any<CancellationToken>()).Returns(trip);

        var sut = CreateSut();
        var result = await sut.Handle(new AcknowledgeTripCommand(trip.Id, op.Id), default);

        result.IsSuccess.Should().BeTrue();
        await _trips.DidNotReceive().TryClaimFromPoolAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _broadcaster.DidNotReceive().BroadcastClaimedAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Pool_OperatorNotFound_Rejects()
    {
        var opId = Guid.NewGuid();
        var trip = PooledTrip();
        _extensions.GetByTripIdAsync(trip.Id, Arg.Any<CancellationToken>()).Returns((ManualTripExtension?)null);
        _operators.GetByIdAsync(opId, Arg.Any<CancellationToken>()).Returns((Operator?)null);

        var sut = CreateSut();
        var result = await sut.Handle(new AcknowledgeTripCommand(trip.Id, opId), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Operator");
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task Handle_Pool_TripNotFound_Rejects()
    {
        var op = MakeOperator("EMP-005", "Eve");
        _extensions.GetByTripIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((ManualTripExtension?)null);
        _operators.GetByIdAsync(op.Id, Arg.Any<CancellationToken>()).Returns(op);
        _trips.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Trip?)null);

        var sut = CreateSut();
        var result = await sut.Handle(new AcknowledgeTripCommand(Guid.NewGuid(), op.Id), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Trip");
        result.Error.Should().Contain("not found");
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static Operator MakeOperator(string employeeCode, string displayName) =>
        Operator.CreateFromJwtClaims(
            employeeCode: employeeCode,
            displayName: displayName,
            role: OperatorRole.Operator);

    // A trip that reached the pool: Status=Created, DispatchedAt set,
    // ClaimedByOperatorId null. Mirrors what ManualDispatchStrategy leaves
    // in the DB after DispatchToPoolAsync.
    private static Trip PooledTrip()
    {
        var trip = Trip.CreateForEnvelope(
            deliveryOrderId: Guid.NewGuid(), upperKey: "UK-POOL-" + Guid.NewGuid().ToString("N")[..6],
            vendorOrderKey: null);
        trip.MarkDispatched(items: Array.Empty<TripItemSnapshot>());
        return trip;
    }

    // A trip that has already been claimed by a specific operator via the
    // SQL CAS. We simulate that here without going through TryClaimFromPoolAsync
    // (which is a raw SQL that our test double doesn't execute).
    private static Trip ClaimedTrip(Guid operatorId)
    {
        var trip = PooledTrip();
        // Reflection helper — Trip has no public setter for ClaimedByOperatorId,
        // and the domain event of claiming happens via the SQL CAS + a reload
        // in prod. For the idempotent-retap test we just need the field visible.
        var field = typeof(Trip).GetProperty(nameof(Trip.ClaimedByOperatorId))!;
        field.SetValue(trip, operatorId);
        return trip;
    }
}
