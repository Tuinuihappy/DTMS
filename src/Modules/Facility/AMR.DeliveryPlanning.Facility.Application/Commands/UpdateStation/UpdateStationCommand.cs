using AMR.DeliveryPlanning.Facility.Domain.Entities;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Facility.Application.Commands.UpdateStation;

public record UpdateStationCommand(
    Guid StationId,
    StationType? Type,
    string? Code) : ICommand;
