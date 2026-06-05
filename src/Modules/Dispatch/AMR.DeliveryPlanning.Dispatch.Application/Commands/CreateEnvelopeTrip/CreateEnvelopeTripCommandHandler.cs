using AMR.DeliveryPlanning.Dispatch.Domain.Entities;
using AMR.DeliveryPlanning.Dispatch.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.Dispatch.Application.Commands.CreateEnvelopeTrip;

public class CreateEnvelopeTripCommandHandler : ICommandHandler<CreateEnvelopeTripCommand, Guid>
{
    private readonly ITripRepository _tripRepository;
    private readonly ILogger<CreateEnvelopeTripCommandHandler> _logger;

    public CreateEnvelopeTripCommandHandler(
        ITripRepository tripRepository,
        ILogger<CreateEnvelopeTripCommandHandler> logger)
    {
        _tripRepository = tripRepository;
        _logger = logger;
    }

    public async Task<Result<Guid>> Handle(CreateEnvelopeTripCommand request, CancellationToken cancellationToken)
    {
        // Idempotency — webhook might race with the dispatcher and create
        // the Trip first. Return the existing TripId if we already have
        // a row for this UpperKey.
        var existing = await _tripRepository.GetByUpperKeyAsync(request.UpperKey, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation("[EnvelopeTrip] Trip already exists for UpperKey {UpperKey} — returning existing TripId {TripId}",
                request.UpperKey, existing.Id);
            return Result<Guid>.Success(existing.Id);
        }

        Trip trip;
        try
        {
            trip = Trip.CreateForEnvelope(
                request.DeliveryOrderId,
                request.UpperKey,
                request.VendorOrderKey,
                request.PickupStationId,
                request.DropStationId,
                request.AttemptNumber,
                request.PreviousAttemptId);
        }
        catch (ArgumentException ex)
        {
            return Result<Guid>.Failure(ex.Message);
        }

        await _tripRepository.AddAsync(trip, cancellationToken);

        _logger.LogInformation(
            "[EnvelopeTrip] ✓ Trip {TripId} created for DeliveryOrder {OrderId} (UpperKey {UpperKey} → VendorOrderKey {VendorKey}, attempt {Attempt}{Retry})",
            trip.Id, request.DeliveryOrderId, request.UpperKey, request.VendorOrderKey,
            request.AttemptNumber,
            request.PreviousAttemptId.HasValue ? $", retryOf {request.PreviousAttemptId.Value}" : "");

        return Result<Guid>.Success(trip.Id);
    }
}
