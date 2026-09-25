using DTMS.Dispatch.Application.Commands.SourceSystemTrip;
using DTMS.Dispatch.Application.Services;
using DTMS.Dispatch.Domain.Entities;
using DTMS.SharedKernel.Messaging;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dispatch.UnitTests;

// POST /api/v1/source/trips/{id}/cancel. The cancel sequence itself is covered
// by TripCancellationServiceTests; what matters here is everything around it —
// who is allowed to ask, what a repeat asks for, and what reaches the service.
public class SourceCancelTripCommandHandlerTests
{
    private const string Owner = "oms";

    [Fact]
    public async Task ForeignTrip_IsRejectedWithoutReachingTheService_EvenWhenAlreadyCancelled()
    {
        // The security case. An already-cancelled trip is the one where the
        // idempotent shortcut is tempting to run first — do that and the
        // 204-vs-400 answer becomes an oracle for probing another system's trip
        // ids. Origin has to be settled before any state is revealed.
        var trip = NewTripInProgress();
        trip.Cancel("someone else stopped it");
        var cancellation = new StubCancellationService();

        var result = await HandlerFor(trip, ownerKey: "sap", cancellation)
            .Handle(Cmd(trip), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("sap");
        cancellation.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task RepeatCancelOnOwnTrip_SucceedsWithoutReachingTheService()
    {
        // A partner that lost the connection before seeing our 204 has no other
        // safe way to find out the cancel landed. Answering 400 would push it
        // into either giving up or escalating a cancel that already happened.
        // Not reaching the service also proves no second CMD_ORDER_CANCEL and no
        // second TripCancelled integration event.
        var trip = NewTripInProgress();
        trip.Cancel("already done");
        var cancellation = new StubCancellationService();

        var result = await HandlerFor(trip, Owner, cancellation).Handle(Cmd(trip), default);

        result.IsSuccess.Should().BeTrue();
        cancellation.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task ActiveTrip_ForwardsActorReasonTimeAndOrigin()
    {
        // The plumbing the whole signature change exists for. A dropped ActionBy
        // or origin leaves the audit row anonymous, and nothing fails loudly —
        // it only shows up when someone later asks who cancelled.
        var trip = NewTripInProgress();
        var actedAt = new DateTime(2026, 9, 25, 3, 0, 0, DateTimeKind.Utc);
        var cancellation = new StubCancellationService();

        var result = await HandlerFor(trip, Owner, cancellation).Handle(
            new SourceCancelTripCommand(trip.Id, Owner, "oms-user-42", "wrong station", actedAt),
            default);

        result.IsSuccess.Should().BeTrue();
        var call = cancellation.Calls.Should().ContainSingle().Which;
        call.TripId.Should().Be(trip.Id);
        call.Reason.Should().Be("wrong station");
        call.ActionBy.Should().Be("oms-user-42");
        call.ActedAt.Should().Be(actedAt);
        call.Origin.Should().Be(Owner);
    }

    [Fact]
    public async Task BlankReason_FallsBackToALabel_AndLeavesTheSystemToOrigin()
    {
        // Reason is non-nullable downstream and lands in the order activity
        // timeline, so it must never arrive empty. It also must not name the
        // calling system: origin already does, and the audit row joins the two
        // as "<reason> via=<origin>".
        var trip = NewTripInProgress();
        var cancellation = new StubCancellationService();

        await HandlerFor(trip, Owner, cancellation).Handle(
            new SourceCancelTripCommand(trip.Id, Owner, "oms-user-42", Reason: "   "), default);

        var call = cancellation.Calls.Should().ContainSingle().Which;
        call.Reason.Should().Be("Cancelled by source system");
        call.Origin.Should().Be(Owner);
    }

    [Fact]
    public async Task ServiceFailure_IsPassedThroughVerbatim()
    {
        // A vendor rejection has to surface as a 400. Collapsing the tail to a
        // bare success would tell the partner the robot stopped when it didn't.
        var trip = NewTripInProgress();
        var cancellation = new StubCancellationService
        {
            NextResult = Result.Failure("Vendor cancel failed: network blip")
        };

        var result = await HandlerFor(trip, Owner, cancellation).Handle(Cmd(trip), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("network blip");
    }

    [Fact]
    public async Task CompletedTrip_IsNotShortCircuited()
    {
        // Guards against widening the Cancelled check into a generic terminal
        // one, which would answer 204 for a trip that actually delivered.
        var trip = NewTripInProgress();
        trip.MarkVendorCompleted();
        var cancellation = new StubCancellationService();

        await HandlerFor(trip, Owner, cancellation).Handle(Cmd(trip), default);

        cancellation.Calls.Should().ContainSingle();
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static SourceCancelTripCommand Cmd(Trip trip)
        => new(trip.Id, Owner, "oms-user-42", "ops");

    private static SourceCancelTripCommandHandler HandlerFor(
        Trip trip, string ownerKey, StubCancellationService cancellation)
        => new(
            new FakeTripRepository(trip),
            new StubOrderStatusReader(null, ownerKey),
            cancellation,
            NullLogger<SourceCancelTripCommandHandler>.Instance);

    private static Trip NewTripInProgress()
    {
        var t = Trip.CreateForEnvelope(Guid.NewGuid(), "abc-G1", "ORD-1");
        t.MarkVendorStarted(Guid.NewGuid());
        return t;
    }
}

internal sealed class StubCancellationService : ITripCancellationService
{
    public record Call(Guid TripId, string Reason, string? ActionBy, DateTime? ActedAt, string? Origin);

    public List<Call> Calls { get; } = new();
    public Result NextResult { get; set; } = Result.Success();

    public Task<Result> CancelAsync(
        Guid tripId, string reason, string? actionBy = null, DateTime? actedAt = null,
        string? origin = null, CancellationToken cancellationToken = default)
    {
        Calls.Add(new Call(tripId, reason, actionBy, actedAt, origin));
        return Task.FromResult(NextResult);
    }
}
