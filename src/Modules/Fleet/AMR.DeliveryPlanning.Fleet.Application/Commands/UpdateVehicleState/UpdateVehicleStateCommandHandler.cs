using AMR.DeliveryPlanning.Fleet.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Exceptions;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Fleet.Application.Commands.UpdateVehicleState;

internal sealed class UpdateVehicleStateCommandHandler : ICommandHandler<UpdateVehicleStateCommand>
{
    private readonly IVehicleRepository _vehicleRepository;

    public UpdateVehicleStateCommandHandler(IVehicleRepository vehicleRepository)
    {
        _vehicleRepository = vehicleRepository;
    }

    public async Task<Result> Handle(UpdateVehicleStateCommand request, CancellationToken cancellationToken)
    {
        var vehicle = await _vehicleRepository.GetByIdAsync(request.VehicleId, cancellationToken);
        if (vehicle is null)
        {
            throw new NotFoundException($"Vehicle with ID {request.VehicleId} not found.");
        }

        vehicle.UpdateState(request.NewState, request.BatteryLevel, request.CurrentNodeId);

        _vehicleRepository.Update(vehicle);
        await _vehicleRepository.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
