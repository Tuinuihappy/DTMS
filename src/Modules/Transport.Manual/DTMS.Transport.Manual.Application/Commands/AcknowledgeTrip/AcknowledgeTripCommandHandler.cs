using AMR.DeliveryPlanning.Dispatch.Domain.Repositories;
using AMR.DeliveryPlanning.Dispatch.Domain.Services;
using DTMS.SharedKernel.Messaging;
using DTMS.Transport.Manual.Domain.Repositories;

namespace DTMS.Transport.Manual.Application.Commands.AcknowledgeTrip;

// Phase 4.6 follow-up — When the operator acknowledges, ALSO transition
// the Trip aggregate from Created → InProgress and emit
// TripStartedDomainEvent carrying the bound-item snapshot. Without this
// step the dispatch-side dispatch.TripItems projection stays empty
// (TripItemsProjector hydrates only on TripStartedIntegrationEvent) +
// the admin Trip detail drawer shows "TRIP ITEMS (0)".
//
// AMR fires this from the RIOT3 webhook (vendor accepted = InProgress);
// Manual mirrors the same semantic with operator acknowledgement —
// the operator IS the vendor for Manual mode, and acknowledging is
// equivalent to RIOT3's TASK_PROCESSING webhook.
internal sealed class AcknowledgeTripCommandHandler : ICommandHandler<AcknowledgeTripCommand>
{
    private readonly IManualTripExtensionRepository _extensions;
    private readonly ITripRepository _trips;
    private readonly ITripItemSnapshotProvider _snapshots;

    public AcknowledgeTripCommandHandler(
        IManualTripExtensionRepository extensions,
        ITripRepository trips,
        ITripItemSnapshotProvider snapshots)
    {
        _extensions = extensions;
        _trips = trips;
        _snapshots = snapshots;
    }

    public async Task<Result> Handle(AcknowledgeTripCommand request, CancellationToken cancellationToken)
    {
        var ext = await _extensions.GetByTripIdAsync(request.TripId, cancellationToken);
        if (ext is null)
            return Result.Failure($"Trip {request.TripId} has no Manual extension — not assigned to an operator.");
        if (ext.OperatorId != request.OperatorId)
            return Result.Failure("Trip is assigned to a different operator.");

        // Idempotent — double-tap returns 204 with no Trip mutation.
        var alreadyAcknowledged = ext.AcknowledgedAt.HasValue;
        ext.MarkAcknowledged();
        await _extensions.SaveChangesAsync(cancellationToken);

        if (alreadyAcknowledged) return Result.Success();

        // Transition the Trip aggregate. Trip.MarkVendorStarted is itself
        // idempotent (early-returns if not Created) so a redelivery is safe.
        var trip = await _trips.GetByIdAsync(request.TripId, cancellationToken);
        if (trip is null) return Result.Success();   // ext exists but trip vanished — defensive
        var items = await _snapshots.GetForTripAsync(request.TripId, cancellationToken);
        trip.MarkVendorStarted(vehicleId: null, vendorVehicleKey: null, vendorVehicleName: null, items: items);
        await _trips.UpdateAsync(trip, cancellationToken);
        return Result.Success();
    }
}
