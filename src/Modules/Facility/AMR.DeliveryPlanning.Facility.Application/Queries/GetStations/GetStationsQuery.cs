using AMR.DeliveryPlanning.Facility.Domain.Entities;
using AMR.DeliveryPlanning.Facility.Domain.Repositories;
using DTMS.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Facility.Application.Queries.GetStations;

// Read-side projection of Station.Actions[intent] — flat data carrier.
public sealed record StationActionDto(
    string ActionType,
    string Category,
    IReadOnlyDictionary<string, string>? Parameters);

public record StationDto(
    Guid Id, Guid MapId, string Name, StationType Type, double X, double Y, double? Theta,
    string? VendorRef, string? Code, bool IsActive,
    Guid? ZoneId, List<string> CompatibleVehicleTypes,
    // Manual override surface — exposes ops force-offline state to UI/dashboards.
    bool ManualOverrideOffline,
    bool IsManualOverrideActive,   // computed: ManualOverrideOffline && not expired
    string? ManualOverrideReason,
    DateTime? ManualOverrideAt,
    string? ManualOverrideBy,
    DateTime? ManualOverrideExpiresAt,
    // Vendor action map keyed by intent (e.g. "lift", "drop", "charge").
    // Null/empty = pure MOVE waypoint.
    IReadOnlyDictionary<string, StationActionDto>? Actions);

public record GetStationsQuery(Guid? MapId, StationType? Type, Guid? ZoneId, string? CompatibleVehicleType, bool IncludeInactive = false, string? Code = null) : IQuery<List<StationDto>>;

public class GetStationsQueryHandler : IQueryHandler<GetStationsQuery, List<StationDto>>
{
    private readonly IStationRepository _repo;
    public GetStationsQueryHandler(IStationRepository repo) => _repo = repo;

    public async Task<Result<List<StationDto>>> Handle(GetStationsQuery request, CancellationToken cancellationToken)
    {
        var stations = await _repo.QueryAsync(request.MapId, request.Type, request.ZoneId, request.CompatibleVehicleType, request.IncludeInactive, request.Code, cancellationToken);
        var now = DateTime.UtcNow;
        var dtos = stations.Select(s => new StationDto(
            s.Id, s.MapId, s.Name, s.Type,
            s.Coordinate.X, s.Coordinate.Y, s.Coordinate.Theta,
            s.VendorRef, s.Code, s.IsActive,
            s.ZoneId, s.CompatibleVehicleTypes,
            s.ManualOverrideOffline,
            s.IsCurrentlyManualOffline(now),
            s.ManualOverrideReason,
            s.ManualOverrideAt,
            s.ManualOverrideBy,
            s.ManualOverrideExpiresAt,
            ProjectActions(s.Actions))).ToList();
        return Result<List<StationDto>>.Success(dtos);
    }

    private static IReadOnlyDictionary<string, StationActionDto>? ProjectActions(
        IReadOnlyDictionary<string, AMR.DeliveryPlanning.Facility.Domain.ValueObjects.StationAction>? actions)
    {
        if (actions is null || actions.Count == 0)
            return null;

        var result = new Dictionary<string, StationActionDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in actions)
            result[kv.Key] = new StationActionDto(kv.Value.ActionType, kv.Value.Category, kv.Value.Parameters);
        return result;
    }
}
