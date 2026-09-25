using DTMS.Dispatch.Application.Services;
using DTMS.Dispatch.Domain.Entities;
using DTMS.Dispatch.Domain.Enums;
using DTMS.Dispatch.Domain.Repositories;
using DTMS.SharedKernel.Messaging;
using Microsoft.Extensions.Logging;

namespace DTMS.Dispatch.Application.Commands.SourceSystemTrip;

public sealed class SourceCancelTripCommandHandler
    : SourceTripCommandHandlerBase<SourceCancelTripCommand>
{
    private readonly ITripCancellationService _cancellation;
    private readonly ILogger<SourceCancelTripCommandHandler> _logger;

    public SourceCancelTripCommandHandler(
        ITripRepository trips,
        IDeliveryOrderStatusReader orders,
        ITripCancellationService cancellation,
        ILogger<SourceCancelTripCommandHandler> logger) : base(trips, orders)
    {
        _cancellation = cancellation;
        _logger = logger;
    }

    // The base already proved this trip belongs to request.SourceSystemKey, so
    // everything below is safe to act on and safe to describe back to the caller.
    protected override async Task<Result> HandleAuthorized(
        SourceCancelTripCommand request, Trip trip, CancellationToken cancellationToken)
    {
        // A repeat cancel answers 204 rather than 400: the caller's intent is
        // already satisfied, and a partner whose connection dropped before it saw
        // our response has no other safe way to find that out.
        //
        // This lives here and nowhere else. Trip.Cancel must keep throwing on an
        // already-cancelled trip, because TripCancellationService's conflict
        // retry reads that throw as "the vendor echo won the race" and preserves
        // the caller's reason on the strength of it.
        //
        // Cancelled only — Completed and Rejected still answer 400. Asking to
        // cancel a trip that already delivered is a real conflict, not a retry.
        if (trip.Status == TripStatus.Cancelled)
        {
            _logger.LogInformation(
                "[SourceTrip] Trip {TripId} already cancelled — repeat from {Key} by {ActionBy} treated as idempotent success",
                trip.Id, request.SourceSystemKey, request.ActionBy);
            return Result.Success();
        }

        var reason = request.Reason?.Trim();
        if (string.IsNullOrWhiteSpace(reason))
        {
            // Warn rather than quietly default. This string is what the order
            // activity timeline shows an operator, and what OMS will read on
            // shipment.cancelled.v1 once that subscription is enabled — if the
            // caller never sends one, we want that visible in the logs before
            // then, not after someone asks why the timeline says nothing.
            _logger.LogWarning(
                "[SourceTrip] Trip {TripId} cancel from {Key} carried no reason — falling back to a generic label",
                trip.Id, request.SourceSystemKey);
            // Which system it was is carried by `origin` below, which the audit
            // row renders as "via=oms" — naming it here too would read as
            // "Cancelled by source system oms via=oms".
            reason = "Cancelled by source system";
        }

        // Don't read `trip` past this point: the service's conflict-retry path
        // calls ResetTracking(), which detaches it.
        return await _cancellation.CancelAsync(
            trip.Id, reason, request.ActionBy, request.ActedAt,
            origin: request.SourceSystemKey, cancellationToken);
    }
}
