using AMR.DeliveryPlanning.Dispatch.Domain.Entities;
using AMR.DeliveryPlanning.Dispatch.Domain.Enums;
using AMR.DeliveryPlanning.Dispatch.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Dispatch.Application.Commands.DispatchTrip;

public class DispatchTripCommandHandler : ICommandHandler<DispatchTripCommand, Guid>
{
    private readonly ITripRepository _tripRepository;

    public DispatchTripCommandHandler(ITripRepository tripRepository)
    {
        _tripRepository = tripRepository;
    }

    public async Task<Result<Guid>> Handle(DispatchTripCommand request, CancellationToken cancellationToken)
    {
        var trip = new Trip(request.JobId, request.VehicleId);

        // Task 1: Move to pickup station
        trip.AddTask(TaskType.Move, 1, request.PickupStationId);
        // Task 2: Lift cargo
        trip.AddTask(TaskType.Lift, 2, request.PickupStationId);
        // Task 3: Move to drop station
        trip.AddTask(TaskType.Move, 3, request.DropStationId);
        // Task 4: Drop cargo
        trip.AddTask(TaskType.Drop, 4, request.DropStationId);

        trip.Start();

        await _tripRepository.AddAsync(trip, cancellationToken);

        return Result<Guid>.Success(trip.Id);
    }
}
