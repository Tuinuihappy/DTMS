using AMR.DeliveryPlanning.Dispatch.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Dispatch.Application.Commands.CancelTrip;

// Operator-initiated cancel marks the local Trip as Cancelled. The
// envelope-side cancel (PUT /api/v4/orders/{upperKey}/operation) is not
// wired in this handler yet — when added, it should run before the local
// state transition so the vendor stops execution first.
public class CancelTripCommandHandler : ICommandHandler<CancelTripCommand>
{
    private readonly ITripRepository _tripRepository;

    public CancelTripCommandHandler(ITripRepository tripRepository)
    {
        _tripRepository = tripRepository;
    }

    public async Task<Result> Handle(CancelTripCommand request, CancellationToken cancellationToken)
    {
        var trip = await _tripRepository.GetByIdAsync(request.TripId, cancellationToken);
        if (trip == null) return Result.Failure($"Trip {request.TripId} not found.");

        try
        {
            trip.Cancel(request.Reason);
            await _tripRepository.UpdateAsync(trip, cancellationToken);
            return Result.Success();
        }
        catch (InvalidOperationException ex) { return Result.Failure(ex.Message); }
    }
}
