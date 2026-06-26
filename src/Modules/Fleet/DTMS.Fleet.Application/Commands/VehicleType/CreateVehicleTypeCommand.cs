using DTMS.SharedKernel.Messaging;

namespace DTMS.Fleet.Application.Commands.VehicleType;

public record CreateVehicleTypeCommand(
    string TypeName,
    double MaxPayload,
    IReadOnlyCollection<string> Capabilities) : ICommand<Guid>;
