using AMR.DeliveryPlanning.Dispatch.Application.Services;
using AMR.DeliveryPlanning.Dispatch.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.Dispatch.Application.Commands.ResumeTrip;

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

        try { trip.Resume(); }
        catch (InvalidOperationException ex) { return Result.Failure(ex.Message); }

        if (string.IsNullOrWhiteSpace(trip.VendorOrderKey))
        {
            _logger.LogWarning("Cannot resume Trip {TripId} — no vendorOrderKey on file (upperKey {UpperKey})",
                trip.Id, trip.UpperKey);
            return Result.Failure(
                "Cannot resume — the vendor never minted an order key for this trip.");
        }

        var vendorResult = await _vendorOps.ResumeAsync(trip.VendorOrderKey, cancellationToken);
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

            return Result.Failure(
                "Cannot resume — the vendor has no record of this order. " +
                "Trip auto-marked Failed; use /reopen on the delivery order then /retry to redispatch if needed.");
        }

        await _tripRepository.UpdateAsync(trip, cancellationToken);
        _logger.LogInformation("Trip {TripId} resumed (vendorOrderKey {OrderKey})", trip.Id, trip.VendorOrderKey);
        return Result.Success();
    }
}
