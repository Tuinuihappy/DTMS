using DTMS.Dispatch.Domain.Repositories;
using DTMS.Dispatch.IntegrationEvents;
using DTMS.Transport.Manual.Application.Services;
using MassTransit;

namespace DTMS.Api.Realtime.Publishers;

/// <summary>
/// Drops a cancelled (or failed / rejected) trip's card from every operator's
/// pool in real time. <see cref="IOperatorPoolBroadcaster.BroadcastRemovedAsync"/>
/// had no caller — the CancelPoolTrip command its contract names was never
/// built — so a trip cancelled while still in the pool stayed on every tablet
/// until the next refetch, and an operator could tap it and get a 409.
///
/// <para>Hosted in DTMS.Api, beside the broadcaster, rather than in
/// Transport.Manual.Application: that assembly isn't scanned for consumers
/// (its PoolTripDispatchedPushConsumer has never been registered). Runs in
/// whichever process takes the message; the SignalR Redis backplane carries
/// the send to the api's connections.</para>
/// </summary>
public sealed class PoolTripRemovedBroadcastConsumer :
    IConsumer<TripCancelledIntegrationEvent>,
    IConsumer<TripFailedIntegrationEvent>,
    IConsumer<TripRejectedIntegrationEventV1>
{
    private readonly ITripRepository _trips;
    private readonly IOperatorPoolBroadcaster _broadcaster;

    public PoolTripRemovedBroadcastConsumer(ITripRepository trips, IOperatorPoolBroadcaster broadcaster)
    {
        _trips = trips;
        _broadcaster = broadcaster;
    }

    public Task Consume(ConsumeContext<TripCancelledIntegrationEvent> ctx)
        => RemoveIfPooledAsync(ctx.Message.TripId, "cancelled", ctx.CancellationToken);

    public Task Consume(ConsumeContext<TripFailedIntegrationEvent> ctx)
        => RemoveIfPooledAsync(ctx.Message.TripId, "failed", ctx.CancellationToken);

    public Task Consume(ConsumeContext<TripRejectedIntegrationEventV1> ctx)
        => RemoveIfPooledAsync(ctx.Message.TripId, "rejected", ctx.CancellationToken);

    private async Task RemoveIfPooledAsync(Guid tripId, string reason, CancellationToken ct)
    {
        var trip = await _trips.GetByIdAsync(tripId, ct);
        if (trip is null || !WasInPool(trip.DispatchedAt, trip.ClaimedByOperatorId, trip.StartedAt, trip.VendorRequestSnapshot))
            return;

        // The broadcaster swallows and logs its own failures — a lost hint
        // only costs the operator a refetch, never a redelivery loop.
        await _broadcaster.BroadcastRemovedAsync(tripId, reason, ct);
    }

    /// <summary>
    /// The trip was sitting in the pool when it ended: dispatched to the pool,
    /// never claimed, never started, and not an AMR trip sent to RIOT3. A
    /// claimed trip already left every tablet's pool through PoolTripClaimed.
    /// </summary>
    internal static bool WasInPool(
        DateTime? dispatchedAt, Guid? claimedByOperatorId, DateTime? startedAt, string? vendorRequestSnapshot)
        => dispatchedAt is not null
           && claimedByOperatorId is null
           && startedAt is null
           && vendorRequestSnapshot is null;
}
