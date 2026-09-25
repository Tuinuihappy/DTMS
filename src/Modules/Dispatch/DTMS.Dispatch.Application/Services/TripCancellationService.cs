using DTMS.Dispatch.Domain.Enums;
using DTMS.Dispatch.Domain.Repositories;
using DTMS.SharedKernel.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DTMS.Dispatch.Application.Services;

// Validate the transition locally first, then instruct the vendor to cancel the
// envelope, then persist. Vendor rejection (a real "no" — auth, business rule,
// etc.) leaves DTMS untouched. A "no-record" outcome (vendor purged / never
// received) is treated as graceful success — the caller's intent is met
// regardless, and refusing would leave the Trip stuck forever with no recovery
// short of a manual DB edit.
public sealed class TripCancellationService : ITripCancellationService
{
    private readonly ITripRepository _tripRepository;
    private readonly IVendorEnvelopeOperationService _vendorOps;
    private readonly ILogger<TripCancellationService> _logger;

    public TripCancellationService(
        ITripRepository tripRepository,
        IVendorEnvelopeOperationService vendorOps,
        ILogger<TripCancellationService> logger)
    {
        _tripRepository = tripRepository;
        _vendorOps = vendorOps;
        _logger = logger;
    }

    public async Task<Result> CancelAsync(
        Guid tripId, string reason, CancellationToken cancellationToken = default)
    {
        var trip = await _tripRepository.GetByIdAsync(tripId, cancellationToken);
        if (trip == null) return Result.Failure($"Trip {tripId} not found.");

        try { trip.Cancel(reason); }
        catch (System.InvalidOperationException ex) { return Result.Failure(ex.Message); }

        // Skip the vendor call when there is no orderKey yet — the trip was
        // marked Cancelled locally but the vendor never minted an id, so
        // there is nothing to cancel on RIOT3. This mirrors the
        // NoVendorRecord outcome (caller intent satisfied).
        if (string.IsNullOrWhiteSpace(trip.VendorOrderKey))
        {
            _logger.LogInformation(
                "Trip {TripId} cancelled locally — no vendorOrderKey on file (upperKey {UpperKey}): {Reason}",
                trip.Id, trip.UpperKey, reason);
            try
            {
                await _tripRepository.UpdateAsync(trip, cancellationToken);
                return Result.Success();
            }
            catch (DbUpdateConcurrencyException)
            {
                return await RetryCancelAfterConflictAsync(tripId, reason, cancellationToken);
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
        // live at the vendor" — the caller's cancel intent is satisfied.
        // We just label the audit event differently.
        if (vendorResult.Value == VendorOperationOutcome.NoVendorRecord)
            _logger.LogInformation(
                "Trip {TripId} cancelled gracefully (vendor had no record of orderKey {OrderKey}): {Reason}",
                trip.Id, trip.VendorOrderKey, reason);
        else
            _logger.LogInformation("Trip {TripId} cancelled (vendorOrderKey {OrderKey}): {Reason}",
                trip.Id, trip.VendorOrderKey, reason);

        try
        {
            await _tripRepository.UpdateAsync(trip, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return await RetryCancelAfterConflictAsync(tripId, reason, cancellationToken);
        }
        return Result.Success();
    }

    // RIOT3's TASK_CANCELED echo (or another writer) committed before our
    // save. Reload and re-apply: if the trip is already Cancelled the echo
    // won — the surviving TripCancelled event carries the vendor's
    // placeholder reason, so we preserve the caller's actual reason as an
    // audit ExecutionEvent (no second integration event). Never repeats the
    // vendor call; a second consecutive conflict propagates (→ HTTP 409).
    private async Task<Result> RetryCancelAfterConflictAsync(
        Guid tripId, string reason, CancellationToken cancellationToken)
    {
        _tripRepository.ResetTracking();
        var trip = await _tripRepository.GetByIdAsync(tripId, cancellationToken);
        if (trip == null) return Result.Failure($"Trip {tripId} not found.");

        var superseded = false;
        try { trip.Cancel(reason); }
        catch (InvalidOperationException) { superseded = true; }

        if (trip.Status != TripStatus.Cancelled)
            return Result.Failure(
                $"Cancel was superseded by a concurrent update — trip is now {trip.Status}.");

        if (superseded)
            trip.RecordSupersededCancelIntent(reason);

        await _tripRepository.UpdateAsync(trip, cancellationToken);
        _logger.LogInformation(
            "Trip {TripId} cancelled — concurrent writer won the race, resolved as no-op (caller reason preserved: {Superseded})",
            trip.Id, superseded);
        return Result.Success();
    }
}
