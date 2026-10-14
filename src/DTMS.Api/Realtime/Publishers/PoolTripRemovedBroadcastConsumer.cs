using DTMS.Dispatch.Domain.Repositories;
using DTMS.Dispatch.IntegrationEvents;
using DTMS.Transport.Manual.Application.Services;
using DTMS.Transport.Manual.Domain.Repositories;
using MassTransit;

namespace DTMS.Api.Realtime.Publishers;

/// <summary>
/// Tells operators in real time when a Manual trip ends on the dispatch side
/// (cancelled / failed / rejected):
/// <list type="bullet">
/// <item>still in the pool → <c>PoolTripRemoved</c> to every operator, so the
/// card leaves every tablet (before, it stayed until a refetch and tapping it
/// ended in a 409 — <see cref="IOperatorPoolBroadcaster.BroadcastRemovedAsync"/>
/// had no caller);</item>
/// <item>already claimed → <c>AssignedTripEnded</c> to the claiming operator
/// only, so their tablet alerts "your trip was cancelled" wherever they
/// are in the app.</item>
/// </list>
///
/// <para>Hosted in DTMS.Api, beside the broadcaster, rather than in
/// Transport.Manual.Application: that assembly isn't scanned for consumers
/// (its PoolTripDispatchedPushConsumer has never been registered). Runs in
/// whichever process takes the message; the SignalR Redis backplane carries
/// the send to the api's connections.</para>
///
/// <para>Keep the class name: it is the RabbitMQ queue name. Renaming leaves
/// the old queue bound to the trip exchanges with no consumer, piling up
/// every event.</para>
/// </summary>
public sealed class PoolTripRemovedBroadcastConsumer :
    IConsumer<TripCancelledIntegrationEvent>,
    IConsumer<TripFailedIntegrationEvent>,
    IConsumer<TripRejectedIntegrationEventV1>
{
    private readonly ITripRepository _trips;
    private readonly IOperatorRepository _operators;
    private readonly IOperatorPoolBroadcaster _broadcaster;

    public PoolTripRemovedBroadcastConsumer(
        ITripRepository trips, IOperatorRepository operators, IOperatorPoolBroadcaster broadcaster)
    {
        _trips = trips;
        _operators = operators;
        _broadcaster = broadcaster;
    }

    public Task Consume(ConsumeContext<TripCancelledIntegrationEvent> ctx)
        => NotifyAsync(ctx.Message.TripId, "cancelled", ctx.Message.Reason, ctx.CancellationToken);

    public Task Consume(ConsumeContext<TripFailedIntegrationEvent> ctx)
        => NotifyAsync(ctx.Message.TripId, "failed", ctx.Message.Reason, ctx.CancellationToken);

    public Task Consume(ConsumeContext<TripRejectedIntegrationEventV1> ctx)
        => NotifyAsync(ctx.Message.TripId, "rejected", ctx.Message.Reason, ctx.CancellationToken);

    private async Task NotifyAsync(Guid tripId, string outcome, string? reason, CancellationToken ct)
    {
        var trip = await _trips.GetByIdAsync(tripId, ct);
        if (trip is null) return;

        // The broadcaster swallows and logs its own failures — a lost hint
        // only costs the operator a refetch, never a redelivery loop.
        if (WasInPool(trip.DispatchedAt, trip.ClaimedByOperatorId, trip.StartedAt, trip.VendorRequestSnapshot))
        {
            await _broadcaster.BroadcastRemovedAsync(tripId, outcome, ct);
            return;
        }

        if (trip.ClaimedByOperatorId is not Guid operatorId) return;   // AMR / self-managed: no operator
        var op = await _operators.GetByIdAsync(operatorId, ct);
        if (op is null || string.IsNullOrWhiteSpace(op.EmployeeCode)) return;

        await _broadcaster.BroadcastAssignedTripEndedAsync(
            op.EmployeeCode, tripId, outcome, reason ?? string.Empty, ct);
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
