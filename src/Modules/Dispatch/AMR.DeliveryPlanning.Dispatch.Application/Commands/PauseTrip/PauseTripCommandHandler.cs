using AMR.DeliveryPlanning.Dispatch.Application.Services;
using AMR.DeliveryPlanning.Dispatch.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.Dispatch.Application.Commands.PauseTrip;

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

        try { trip.Pause(); }
        catch (InvalidOperationException ex) { return Result.Failure(ex.Message); }

        var vendorResult = await _vendorOps.PauseAsync(trip.UpperKey, cancellationToken);
        if (vendorResult.IsFailure)
        {
            _logger.LogWarning("Vendor pause rejected for Trip {TripId} (upperKey {UpperKey}): {Error}",
                trip.Id, trip.UpperKey, vendorResult.Error);
            return Result.Failure($"Vendor pause failed: {vendorResult.Error}");
        }

        await _tripRepository.UpdateAsync(trip, cancellationToken);
        _logger.LogInformation("Trip {TripId} paused (upperKey {UpperKey})", trip.Id, trip.UpperKey);
        return Result.Success();
    }
}
