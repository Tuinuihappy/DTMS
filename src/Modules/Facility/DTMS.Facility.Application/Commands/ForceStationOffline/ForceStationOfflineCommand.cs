using DTMS.SharedKernel.Messaging;

namespace DTMS.Facility.Application.Commands.ForceStationOffline;

public record ForceStationOfflineCommand(
    Guid StationId,
    string Reason,
    int DurationMinutes,
    string? By = null
) : ICommand;
