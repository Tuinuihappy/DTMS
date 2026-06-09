using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Facility.Application.Commands.SyncMapStations;

public record SyncMapStationsCommand(Guid MapId) : ICommand<SyncMapStationsResult>;

public record SyncMapStationsResult(
    Guid MapId,
    string MapName,
    int Added,
    int Updated,
    int Reactivated,
    int Deactivated);
