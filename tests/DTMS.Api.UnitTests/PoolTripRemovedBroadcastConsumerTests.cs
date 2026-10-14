using DTMS.Api.Realtime.Publishers;
using DTMS.Dispatch.Domain.Entities;
using DTMS.Dispatch.Domain.Repositories;
using DTMS.Dispatch.IntegrationEvents;
using DTMS.Transport.Manual.Application.Services;
using DTMS.Transport.Manual.Domain.Entities;
using DTMS.Transport.Manual.Domain.Enums;
using DTMS.Transport.Manual.Domain.Repositories;
using MassTransit;
using NSubstitute;

namespace DTMS.Api.UnitTests;

// A trip that ends while still sitting in the operator pool must leave every
// tablet's pool at once (PoolTripRemoved). Trips that already left the pool —
// claimed/started — or never entered it must not broadcast.
public class PoolTripRemovedBroadcastConsumerTests
{
    private static readonly Guid Pickup = Guid.NewGuid();
    private static readonly Guid Drop = Guid.NewGuid();

    private readonly ITripRepository _trips = Substitute.For<ITripRepository>();
    private readonly IOperatorPoolBroadcaster _broadcaster = Substitute.For<IOperatorPoolBroadcaster>();

    private readonly IOperatorRepository _operators = Substitute.For<IOperatorRepository>();

    private PoolTripRemovedBroadcastConsumer Consumer() => new(_trips, _operators, _broadcaster);

    private static Trip PooledTrip()
    {
        var trip = Trip.CreateForEnvelope(Guid.NewGuid(), "upper-G1", "ORD-1", Pickup, Drop);
        trip.MarkDispatched();
        return trip;
    }

    private static ConsumeContext<T> Ctx<T>(T message) where T : class
    {
        var ctx = Substitute.For<ConsumeContext<T>>();
        ctx.Message.Returns(message);
        ctx.CancellationToken.Returns(CancellationToken.None);
        return ctx;
    }

    private static TripCancelledIntegrationEvent Cancelled(Guid tripId) =>
        new(Guid.NewGuid(), DateTime.UtcNow, tripId, Guid.NewGuid(), Guid.NewGuid(), "order cancelled", null);

    [Fact]
    public async Task PooledTrip_Cancelled_BroadcastsRemoved()
    {
        var trip = PooledTrip();
        _trips.GetByIdAsync(trip.Id, Arg.Any<CancellationToken>()).Returns(trip);

        await Consumer().Consume(Ctx(Cancelled(trip.Id)));

        await _broadcaster.Received(1).BroadcastRemovedAsync(trip.Id, "cancelled", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PooledTrip_Failed_BroadcastsRemoved()
    {
        var trip = PooledTrip();
        _trips.GetByIdAsync(trip.Id, Arg.Any<CancellationToken>()).Returns(trip);

        await Consumer().Consume(Ctx(new TripFailedIntegrationEvent(
            Guid.NewGuid(), DateTime.UtcNow, trip.Id, Guid.NewGuid(), Guid.NewGuid(), "boom", "upper-G1")));

        await _broadcaster.Received(1).BroadcastRemovedAsync(trip.Id, "failed", Arg.Any<CancellationToken>());
    }

    // A claim starts the trip (Created → InProgress), and the claim already
    // removed the card through PoolTripClaimed.
    [Fact]
    public async Task StartedTrip_Cancelled_DoesNotBroadcast()
    {
        var trip = PooledTrip();
        trip.MarkVendorStarted(vendorVehicleKey: null, vendorVehicleName: null);
        _trips.GetByIdAsync(trip.Id, Arg.Any<CancellationToken>()).Returns(trip);

        await Consumer().Consume(Ctx(Cancelled(trip.Id)));

        await _broadcaster.DidNotReceiveWithAnyArgs().BroadcastRemovedAsync(default, default!, default);
    }

    [Fact]
    public async Task NeverDispatchedTrip_Cancelled_DoesNotBroadcast()
    {
        var trip = Trip.CreateForEnvelope(Guid.NewGuid(), "upper-G1", "ORD-1", Pickup, Drop);
        _trips.GetByIdAsync(trip.Id, Arg.Any<CancellationToken>()).Returns(trip);

        await Consumer().Consume(Ctx(Cancelled(trip.Id)));

        await _broadcaster.DidNotReceiveWithAnyArgs().BroadcastRemovedAsync(default, default!, default);
    }

    [Fact]
    public async Task UnknownTrip_DoesNotBroadcast()
    {
        var tripId = Guid.NewGuid();
        _trips.GetByIdAsync(tripId, Arg.Any<CancellationToken>()).Returns((Trip?)null);

        await Consumer().Consume(Ctx(Cancelled(tripId)));

        await _broadcaster.DidNotReceiveWithAnyArgs().BroadcastRemovedAsync(default, default!, default);
    }

    // ── claimed trips: tell the claiming operator only ───────────────────

    // A pool claim sets ClaimedByOperatorId by raw-SQL CAS
    // (TripRepository.TryClaimFromPoolAsync), so the domain has no setter.
    private static Trip ClaimedTrip(Guid operatorId)
    {
        var trip = PooledTrip();
        typeof(Trip).GetProperty(nameof(Trip.ClaimedByOperatorId))!.SetValue(trip, operatorId);
        trip.MarkVendorStarted(vendorVehicleKey: null, vendorVehicleName: null);
        return trip;
    }

    [Fact]
    public async Task ClaimedTrip_Cancelled_AlertsTheClaimingOperatorOnly()
    {
        var op = Operator.CreateFromJwtClaims("EMP0142", "สมชาย ใจดี", OperatorRole.Operator);
        var trip = ClaimedTrip(op.Id);
        _trips.GetByIdAsync(trip.Id, Arg.Any<CancellationToken>()).Returns(trip);
        _operators.GetByIdAsync(op.Id, Arg.Any<CancellationToken>()).Returns(op);

        await Consumer().Consume(Ctx(Cancelled(trip.Id)));

        await _broadcaster.Received(1).BroadcastAssignedTripEndedAsync(
            "EMP0142", trip.Id, "cancelled", "order cancelled", Arg.Any<CancellationToken>());
        await _broadcaster.DidNotReceiveWithAnyArgs().BroadcastRemovedAsync(default, default!, default);
    }

    [Fact]
    public async Task ClaimedTrip_OperatorGone_DoesNotBroadcast()
    {
        var trip = ClaimedTrip(Guid.NewGuid());
        _trips.GetByIdAsync(trip.Id, Arg.Any<CancellationToken>()).Returns(trip);
        _operators.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Operator?)null);

        await Consumer().Consume(Ctx(Cancelled(trip.Id)));

        await _broadcaster.DidNotReceiveWithAnyArgs().BroadcastAssignedTripEndedAsync(
            default!, default, default!, default!, default);
    }

    // Started without a claim = AMR or self-managed: no operator to tell.
    [Fact]
    public async Task UnclaimedStartedTrip_Cancelled_AlertsNobody()
    {
        var trip = PooledTrip();
        trip.MarkVendorStarted(vendorVehicleKey: null, vendorVehicleName: null);
        _trips.GetByIdAsync(trip.Id, Arg.Any<CancellationToken>()).Returns(trip);

        await Consumer().Consume(Ctx(Cancelled(trip.Id)));

        await _broadcaster.DidNotReceiveWithAnyArgs().BroadcastAssignedTripEndedAsync(
            default!, default, default!, default!, default);
        await _operators.DidNotReceiveWithAnyArgs().GetByIdAsync(default, default);
    }
}
