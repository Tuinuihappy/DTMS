using DTMS.Dispatch.Application.Services;
using DTMS.Dispatch.Domain.Enums;
using DTMS.Dispatch.Domain.Repositories;
using DTMS.SharedKernel.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DTMS.Dispatch.Application.Commands.CancelTrip;

// Operator-initiated cancel: validate the transition locally first, then
// instruct the vendor to cancel the envelope, then persist. Vendor
// rejection (a real "no" — auth, business rule, etc.) leaves DTMS
// untouched. A "no-record" outcome (vendor purged / never received) is
// treated as graceful success — the operator's intent is met regardless,
// and refusing would leave the Trip stuck forever with no recovery
// short of a manual DB edit.
public class CancelTripCommandHandler : ICommandHandler<CancelTripCommand>
{
    private readonly ITripRepository _tripRepository;
    private readonly IVendorEnvelopeOperationService _vendorOps;
    private readonly ILogger<CancelTripCommandHandler> _logger;

    public CancelTripCommandHandler(
        ITripRepository tripRepository,
        IVendorEnvelopeOperationService vendorOps,
        ILogger<CancelTripCommandHandler> logger)
    {
        _tripRepository = tripRepository;
        _vendorOps = vendorOps;
        _logger = logger;
    }

    public async Task<Result> Handle(CancelTripCommand request, CancellationToken cancellationToken)
    {
        var trip = await _tripRepository.GetByIdAsync(request.TripId, cancellationToken);
        if (trip == null) return Result.Failure($"Trip {request.TripId} not found.");

        try { trip.Cancel(request.Reason); }
        catch (System.InvalidOperationException ex) { return Result.Failure(ex.Message); }

        // Skip the vendor call when there is no orderKey yet — the trip was
        // marked Cancelled locally but the vendor never minted an id, so
        // there is nothing to cancel on RIOT3. This mirrors the
        // NoVendorRecord outcome (operator intent satisfied).
        if (string.IsNullOrWhiteSpace(trip.VendorOrderKey))
        {
            _logger.LogInformation(
                "Trip {TripId} cancelled locally — no vendorOrderKey on file (upperKey {UpperKey}): {Reason}",
                trip.Id, trip.UpperKey, request.Reason);
            try
            {
                await _tripRepository.UpdateAsync(trip, cancellationToken);
                return Result.Success();
            }
            catch (DbUpdateConcurrencyException)
            {
                return await RetryCancelAfterConflictAsync(request, cancellationToken);
            }
        }

        var vendorResult = await _vendorOps.CancelAsync(trip.VendorOrderKey, cancellationToken);
        if (vendorResult.IsFailure)
        {
            _logger.LogWarning("Vendor cancel rejected for Trip {TripId} (vendorOrderKey {OrderKey}): {Error}",
                trip.Id, trip.VendorOrderKey, vendorResult.Error);
            return Result.Failure($"Vendor cancel failed: {vendorResult.Error}");
        }

        // Both Accepted and NoVendorRecord mean "the order is no longer
        // live at the vendor" — the operator's cancel intent is satisfied.
        // We just label the audit event differently.
        if (vendorResult.Value == VendorOperationOutcome.NoVendorRecord)
            _logger.LogInformation(
                "Trip {TripId} cancelled gracefully (vendor had no record of orderKey {OrderKey}): {Reason}",
                trip.Id, trip.VendorOrderKey, request.Reason);
        else
            _logger.LogInformation("Trip {TripId} cancelled (vendorOrderKey {OrderKey}): {Reason}",
                trip.Id, trip.VendorOrderKey, request.Reason);

        try
        {
            await _tripRepository.UpdateAsync(trip, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return await RetryCancelAfterConflictAsync(request, cancellationToken);
        }
        return Result.Success();
    }

    // RIOT3's TASK_CANCELED echo (or another writer) committed before our
    // save. Reload and re-apply: if the trip is already Cancelled the echo
    // won — the surviving TripCancelled event carries the vendor's
    // placeholder reason, so we preserve the operator's actual reason as an
    // audit ExecutionEvent (no second integration event). Never repeats the
    // vendor call; a second consecutive conflict propagates (→ HTTP 409).
    private async Task<Result> RetryCancelAfterConflictAsync(
        CancelTripCommand request, CancellationToken cancellationToken)
    {
        _tripRepository.ResetTracking();
        var trip = await _tripRepository.GetByIdAsync(request.TripId, cancellationToken);
        if (trip == null) return Result.Failure($"Trip {request.TripId} not found.");

        var superseded = false;
        try { trip.Cancel(request.Reason); }
        catch (InvalidOperationException) { superseded = true; }

        if (trip.Status != TripStatus.Cancelled)
            return Result.Failure(
                $"Cancel was superseded by a concurrent update — trip is now {trip.Status}.");

        if (superseded)
            trip.RecordSupersededCancelIntent(request.Reason);

        await _tripRepository.UpdateAsync(trip, cancellationToken);
        _logger.LogInformation(
            "Trip {TripId} cancelled — concurrent writer won the race, resolved as no-op (operator reason preserved: {Superseded})",
            trip.Id, superseded);
        return Result.Success();
    }
}
