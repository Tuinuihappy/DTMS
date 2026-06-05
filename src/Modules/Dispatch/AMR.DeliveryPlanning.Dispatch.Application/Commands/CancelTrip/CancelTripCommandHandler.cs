using AMR.DeliveryPlanning.Dispatch.Application.Services;
using AMR.DeliveryPlanning.Dispatch.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.Dispatch.Application.Commands.CancelTrip;

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

        var vendorResult = await _vendorOps.CancelAsync(trip.UpperKey, cancellationToken);
        if (vendorResult.IsFailure)
        {
            _logger.LogWarning("Vendor cancel rejected for Trip {TripId} (upperKey {UpperKey}): {Error}",
                trip.Id, trip.UpperKey, vendorResult.Error);
            return Result.Failure($"Vendor cancel failed: {vendorResult.Error}");
        }

        // Both Accepted and NoVendorRecord mean "the order is no longer
        // live at the vendor" — the operator's cancel intent is satisfied.
        // We just label the audit event differently.
        if (vendorResult.Value == VendorOperationOutcome.NoVendorRecord)
            _logger.LogInformation(
                "Trip {TripId} cancelled gracefully (vendor had no record of upperKey {UpperKey}): {Reason}",
                trip.Id, trip.UpperKey, request.Reason);
        else
            _logger.LogInformation("Trip {TripId} cancelled (upperKey {UpperKey}): {Reason}",
                trip.Id, trip.UpperKey, request.Reason);

        await _tripRepository.UpdateAsync(trip, cancellationToken);
        return Result.Success();
    }
}
