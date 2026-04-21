using AMR.DeliveryPlanning.Facility.Domain.Entities;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Facility.Application.Commands.AddStation;

public record AddStationCommand(
    Guid MapId,
    string Name,
    double X,
    double Y,
    double? Theta,
    StationType Type) : ICommand<Guid>;
