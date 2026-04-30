using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Fleet.Application.Commands.VehicleType;

public record CreateVehicleTypeCommand(
    string TypeName,
    double MaxPayload,
    IReadOnlyCollection<string> Capabilities) : ICommand<Guid>;
