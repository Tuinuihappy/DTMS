using DTMS.Api.Realtime.Publishers;
using DTMS.Dispatch.Domain.Entities;
using DTMS.Dispatch.Domain.Repositories;
using DTMS.Dispatch.IntegrationEvents;
using DTMS.Transport.Manual.Application.Services;
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

    private PoolTripRemovedBroadcastConsumer Consumer() => new(_trips, _broadcaster);

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
}
