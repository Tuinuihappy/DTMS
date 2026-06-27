using DTMS.Dispatch.Application.Services;
using DTMS.Dispatch.Domain.Enums;
using DTMS.Dispatch.Domain.Repositories;
using DTMS.SharedKernel.Messaging;
using Microsoft.Extensions.Logging;

namespace DTMS.Dispatch.Application.Commands.PauseTrip;

// Operator-initiated pause. Unlike cancel, pause's intent is "freeze for
// later resume" — if the vendor doesn't have the order anymore, there's
// nothing to freeze and the operator's intent CAN'T be met. We surface
// that mismatch by marking the Trip Failed (vendor is the source of
// truth on execution state) and returning an error that points the
// operator at the next step.
public class PauseTripCommandHandler : ICommandHandler<PauseTripCommand>
{
    private readonly ITripRepository _tripRepository;
    private readonly IVendorEnvelopeOperationService _vendorOps;
    private readonly ILogger<PauseTripCommandHandler> _logger;

    public PauseTripCommandHandler(
        ITripRepository tripRepository,
        IVendorEnvelopeOperationService vendorOps,
        ILogger<PauseTripCommandHandler> logger)
    {
        _tripRepository = tripRepository;
        _vendorOps = vendorOps;
        _logger = logger;
    }

    public async Task<Result> Handle(PauseTripCommand request, CancellationToken cancellationToken)
    {
        var trip = await _tripRepository.GetByIdAsync(request.TripId, cancellationToken);
        if (trip == null) return Result.Failure($"Trip {request.TripId} not found.");

        // Operator-initiated pause → vendor side will be HELD → resume must
        // use CONTINUE_FROM_HELD.
        try { trip.Pause(VendorPauseSource.Held); }
        catch (InvalidOperationException ex) { return Result.Failure(ex.Message); }

        // No orderKey → nothing live at the vendor to freeze. Pause's
        // intent can't be satisfied, so reject early instead of pretending.
        if (string.IsNullOrWhiteSpace(trip.VendorOrderKey))
        {
            _logger.LogWarning("Cannot pause Trip {TripId} — no vendorOrderKey on file (upperKey {UpperKey})",
                trip.Id, trip.UpperKey);
            return Result.Failure(
                "Cannot pause — the vendor never minted an order key for this trip.");
        }

        var vendorResult = await _vendorOps.PauseAsync(trip.VendorOrderKey, cancellationToken);
        if (vendorResult.IsFailure)
        {
            _logger.LogWarning("Vendor pause rejected for Trip {TripId} (vendorOrderKey {OrderKey}): {Error}",
                trip.Id, trip.VendorOrderKey, vendorResult.Error);
            return Result.Failure($"Vendor pause failed: {vendorResult.Error}");
        }

        // Vendor has no record of this order — pause is impossible to
        // satisfy. Reconcile the Trip to Failed so it stops showing up in
        // in-flight queries, and tell the operator the next step.
        if (vendorResult.Value == VendorOperationOutcome.NoVendorRecord)
        {
            const string reason = "Vendor has no record of the order at pause time — auto-reconciled.";
            try
            {
                // Discard the in-memory Pause we already applied; MarkVendorFailed
                // expects the trip not to be in a terminal state — Created /
                // InProgress / Paused are all eligible.
                trip.MarkVendorFailed(reason);
                await _tripRepository.UpdateAsync(trip, cancellationToken);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning("Auto-reconcile failed for Trip {TripId} after pause NoVendorRecord: {Error}",
                    trip.Id, ex.Message);
            }

            return Result.Failure(
                "Cannot pause — the vendor has no record of this order. " +
                "Trip auto-marked Failed; use /reopen on the delivery order then /retry to redispatch if needed.");
        }

        await _tripRepository.UpdateAsync(trip, cancellationToken);
        _logger.LogInformation("Trip {TripId} paused (vendorOrderKey {OrderKey})", trip.Id, trip.VendorOrderKey);
        return Result.Success();
    }
}
