using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Facility.Application.Commands.ForceStationOffline;

public record ForceStationOfflineCommand(
    Guid StationId,
    string Reason,
    int DurationMinutes,
    string? By = null
) : ICommand;
