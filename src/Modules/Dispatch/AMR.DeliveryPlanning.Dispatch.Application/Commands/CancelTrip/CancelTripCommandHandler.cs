using AMR.DeliveryPlanning.Dispatch.Application.Services;
using AMR.DeliveryPlanning.Dispatch.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.Dispatch.Application.Commands.CancelTrip;

// Operator-initiated cancel: validate the transition locally first, then
// instruct the vendor to cancel the envelope, then persist. Vendor
// rejection leaves DTMS untouched (the in-memory mutation is discarded
// when the scope disposes) so the next operator action sees consistent
// state with what RIOT3 actually has.
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
        catch (InvalidOperationException ex) { return Result.Failure(ex.Message); }

        var vendorResult = await _vendorOps.CancelAsync(trip.UpperKey, cancellationToken);
        if (vendorResult.IsFailure)
        {
            _logger.LogWarning("Vendor cancel rejected for Trip {TripId} (upperKey {UpperKey}): {Error}",
                trip.Id, trip.UpperKey, vendorResult.Error);
            return Result.Failure($"Vendor cancel failed: {vendorResult.Error}");
        }

        await _tripRepository.UpdateAsync(trip, cancellationToken);
        _logger.LogInformation("Trip {TripId} cancelled (upperKey {UpperKey}): {Reason}",
            trip.Id, trip.UpperKey, request.Reason);
        return Result.Success();
    }
}
