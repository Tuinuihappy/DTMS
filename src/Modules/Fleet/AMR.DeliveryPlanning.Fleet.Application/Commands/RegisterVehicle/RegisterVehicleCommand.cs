using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Fleet.Application.Commands.RegisterVehicle;

public record RegisterVehicleCommand(
    string VehicleName,
    Guid VehicleTypeId) : ICommand<Guid>;
