using DTMS.SharedKernel.Messaging;

namespace DTMS.Facility.Application.Commands.SyncMapStations;

public record SyncMapStationsCommand(Guid MapId) : ICommand<SyncMapStationsResult>;

// Updated counts only stations whose name/coords actually changed.
// Rebound = a RIOT3 station re-created under a new id that took over the
// removed row with the same name/code (keeps its Id, Type and Code).
// CodeConflicts = new stations added WITHOUT a Code because it was already
// taken on this map — they need an operator to assign one in the editor.
public record SyncMapStationsResult(
    Guid MapId,
    string MapName,
    int Added,
    int Updated,
    int Reactivated,
    int Deactivated,
    int Rebound = 0,
    int CodeConflicts = 0);
