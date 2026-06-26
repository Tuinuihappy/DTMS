using DTMS.SharedKernel.Messaging;

namespace DTMS.Fleet.Application.Commands.VehicleGroup;

public record CreateVehicleGroupCommand(string Name, string Description, List<string>? Tags) : ICommand<Guid>;

public record AddVehicleToGroupCommand(Guid GroupId, Guid VehicleId) : ICommand;

public record RemoveVehicleFromGroupCommand(Guid GroupId, Guid VehicleId) : ICommand;
