using DTMS.SharedKernel.Messaging;

namespace DTMS.Facility.Application.Commands.SyncMapStations;

// ConfirmMassDeactivation: the operator saw the held list and confirmed it
// (manual sync only — the poller never sets it).
public record SyncMapStationsCommand(Guid MapId, bool ConfirmMassDeactivation = false)
    : ICommand<SyncMapStationsResult>;

// Updated counts only stations whose name/coords actually changed.
// Rebound = a RIOT3 station re-created under a new id that took over the
// removed row with the same name/code (keeps its Id, Type and Code).
// CodeConflicts = new stations added WITHOUT a Code because it was already
// taken on this map — they need an operator to assign one in the editor.
// HeldStations = stations RIOT3 no longer lists that were NOT deactivated
// because there were more than StationSyncOptions.MaxDeactivateFraction of
// the active ones; resend with ConfirmMassDeactivation to deactivate them.
public record SyncMapStationsResult(
    Guid MapId,
    string MapName,
    int Added,
    int Updated,
    int Reactivated,
    int Deactivated,
    int Rebound = 0,
    int CodeConflicts = 0,
    IReadOnlyList<string>? HeldStations = null);
