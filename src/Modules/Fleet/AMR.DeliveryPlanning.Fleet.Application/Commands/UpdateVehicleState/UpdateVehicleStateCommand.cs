using AMR.DeliveryPlanning.Fleet.Domain.Enums;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Fleet.Application.Commands.UpdateVehicleState;

public record UpdateVehicleStateCommand(
    Guid VehicleId,
    VehicleState NewState,
    double BatteryLevel,
    Guid? CurrentNodeId) : ICommand;
