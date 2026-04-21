using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Facility.Application.Commands.CreateMap;

public record CreateMapCommand(
    string Name,
    string Version,
    double Width,
    double Height,
    string MapData) : ICommand<Guid>;
