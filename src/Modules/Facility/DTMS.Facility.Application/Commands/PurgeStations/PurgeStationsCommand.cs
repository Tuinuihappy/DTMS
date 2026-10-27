using DTMS.SharedKernel.Messaging;

namespace DTMS.Facility.Application.Commands.PurgeStations;

/// <summary>
/// Hard-deletes stations RIOT3 has removed from a map. StationIds null =
/// every inactive RIOT3 station on the map. Each candidate is re-checked
/// against RIOT3 live and against what other modules still use; the ones
/// that fail are skipped with a reason, the rest are deleted.
/// </summary>
public record PurgeStationsCommand(Guid MapId, IReadOnlyList<Guid>? StationIds = null)
    : ICommand<PurgeStationsResult>;

public record PurgedStation(Guid Id, string Label);

public record SkippedStation(Guid Id, string Label, string Reason);

public record PurgeStationsResult(
    IReadOnlyList<PurgedStation> Purged,
    IReadOnlyList<SkippedStation> Skipped);
