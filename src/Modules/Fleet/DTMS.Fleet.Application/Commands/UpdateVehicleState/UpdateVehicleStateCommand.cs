using DTMS.Fleet.Domain.Enums;
using DTMS.SharedKernel.Messaging;

namespace DTMS.Fleet.Application.Commands.UpdateVehicleState;

public record UpdateVehicleStateCommand(
    Guid VehicleId,
    VehicleState NewState,
    double BatteryLevel,
    Guid? CurrentNodeId) : ICommand;
