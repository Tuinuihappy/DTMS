using DTMS.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Facility.Application.Commands.ClearStationOverride;

public record ClearStationOverrideCommand(Guid StationId) : ICommand;
