using DTMS.Dispatch.Application.Services;
using DTMS.Dispatch.Domain.Enums;
using DTMS.Dispatch.Domain.Repositories;
using DTMS.SharedKernel.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DTMS.Dispatch.Application.Commands.ResumeTrip;

// Operator-initiated resume. Mirrors Pause's NoVendorRecord policy: if
// the vendor doesn't have the order anymore, there's nothing to resume
// and DTMS reconciles the Trip to Failed so it stops showing up as
// in-flight.
public class ResumeTripCommandHandler : ICommandHandler<ResumeTripCommand>
{
    private readonly ITripRepository _tripRepository;
    private readonly IVendorEnvelopeOperationService _vendorOps;
    private readonly ILogger<ResumeTripCommandHandler> _logger;

    public ResumeTripCommandHandler(
        ITripRepository tripRepository,
        IVendorEnvelopeOperationService vendorOps,
        ILogger<ResumeTripCommandHandler> logger)
    {
        _tripRepository = tripRepository;
        _vendorOps = vendorOps;
        _logger = logger;
    }

    public async Task<Result> Handle(ResumeTripCommand request, CancellationToken cancellationToken)
    {
        var trip = await _tripRepository.GetByIdAsync(request.TripId, cancellationToken);
        if (trip == null) return Result.Failure($"Trip {request.TripId} not found.");

        // Capture the flavour BEFORE Resume() flips the status back to
        // InProgress. Status is the single source of truth since the
        // Hang/Held split — no legacy-null fallback ambiguity anymore.
        var wasHang = trip.Status == TripStatus.Hang;

        try { trip.Resume(); }
        catch (InvalidOperationException ex) { return Result.Failure(ex.Message); }

        if (string.IsNullOrWhiteSpace(trip.VendorOrderKey))
        {
            _logger.LogWarning("Cannot resume Trip {TripId} — no vendorOrderKey on file (upperKey {UpperKey})",
                trip.Id, trip.UpperKey);
            return Result.Failure(
                "Cannot resume — the vendor never minted an order key for this trip.");
        }

        // Branch by vendor-side state: Held → CONTINUE_FROM_HELD;
        // Hang → CONTINUE_FROM_HANG. Crossing them produces E639999
        // "multi-level template fill error" from RIOT3.
        var vendorResult = wasHang
            ? await _vendorOps.ResumeFromHangAsync(trip.VendorOrderKey, cancellationToken)
            : await _vendorOps.ResumeAsync(trip.VendorOrderKey, cancellationToken);
        if (vendorResult.IsFailure)
        {
            _logger.LogWarning("Vendor resume rejected for Trip {TripId} (vendorOrderKey {OrderKey}): {Error}",
                trip.Id, trip.VendorOrderKey, vendorResult.Error);
            return Result.Failure($"Vendor resume failed: {vendorResult.Error}");
        }

        if (vendorResult.Value == VendorOperationOutcome.NoVendorRecord)
        {
            const string reason = "Vendor has no record of the order at resume time — auto-reconciled.";
            try
            {
                trip.MarkVendorFailed(reason);
                await _tripRepository.UpdateAsync(trip, cancellationToken);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning("Auto-reconcile failed for Trip {TripId} after resume NoVendorRecord: {Error}",
                    trip.Id, ex.Message);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Another writer beat the auto-reconcile save. Reload and
                // re-apply; MarkVendorFailed no-ops if a terminal state
                // already landed (first-terminal-wins).
                _tripRepository.ResetTracking();
                trip = await _tripRepository.GetByIdAsync(request.TripId, cancellationToken);
                if (trip is not null)
                {
                    try
                    {
                        trip.MarkVendorFailed(reason);
                        await _tripRepository.UpdateAsync(trip, cancellationToken);
                    }
                    catch (InvalidOperationException) { /* superseded */ }
                }
            }

            return Result.Failure(
                "Cannot resume — the vendor has no record of this order. " +
                "Trip auto-marked Failed; use /reopen on the delivery order then /retry to redispatch if needed.");
        }

        try
        {
            await _tripRepository.UpdateAsync(trip, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // RIOT3 accepted the resume and its TASK_*_TO_CONTINUE echo
            // webhook committed before we did. Reload and re-apply: Resume
            // throws InvalidOperationException when the trip is already
            // InProgress (echo won), which we treat as intent-satisfied.
            // Never repeat the vendor call; a second consecutive conflict
            // propagates (→ HTTP 409).
            _tripRepository.ResetTracking();
            trip = await _tripRepository.GetByIdAsync(request.TripId, cancellationToken);
            if (trip == null) return Result.Failure($"Trip {request.TripId} not found.");

            try { trip.Resume(); }
            catch (InvalidOperationException) { /* status check below decides */ }

            if (trip.Status != TripStatus.InProgress)
                return Result.Failure(
                    $"Resume was superseded by a concurrent update — trip is now {trip.Status}.");

            await _tripRepository.UpdateAsync(trip, cancellationToken);
            _logger.LogInformation(
                "Trip {TripId} resumed (vendorOrderKey {OrderKey}, source {Source}) — vendor echo won the race, resolved as no-op",
                trip.Id, trip.VendorOrderKey, wasHang ? "Hang" : "Held");
            return Result.Success();
        }

        _logger.LogInformation("Trip {TripId} resumed (vendorOrderKey {OrderKey}, source {Source})",
            trip.Id, trip.VendorOrderKey, wasHang ? "Hang" : "Held");
        return Result.Success();
    }
}
