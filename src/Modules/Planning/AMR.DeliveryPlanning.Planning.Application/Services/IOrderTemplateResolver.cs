using AMR.DeliveryPlanning.Planning.Domain.Entities;

namespace AMR.DeliveryPlanning.Planning.Application.Services;

// Output of OrderTemplate resolution — what we would POST to RIOT3 /api/v4/orders
// after expanding every ActionTemplateName reference into inline action params.
// Keeps the contract free of vendor types so callers (and tests) can inspect
// the result without dragging in RIOT3 model classes.
public sealed record ResolvedOrder(
    string Name,
    int Priority,
    string StructureType,
    int TransportOrderPriority,
    IReadOnlyList<ResolvedMission> Missions,
    string? AppointVehicleKey,
    string? AppointVehicleName,
    string? AppointVehicleGroupKey,
    string? AppointVehicleGroupName,
    string? AppointQueueWaitArea);

public sealed record ResolvedMission(
    int Sequence,
    string Type,                                 // "MOVE" | "ACT"
    string Category,
    int? MapId,
    int? StationId,
    string? ActionType,
    string? BlockingType,
    IReadOnlyList<ResolvedParam>? ActionParameters,
    // Display label for ACT missions — surfaces in the RIOT3 operator UI.
    // Set to the ActionTemplate.Name for reference-path ACTs; null for
    // inline ACTs and for MOVE missions.
    string? ActionName = null);

// Value flows through as object so int / string params keep their JSON type.
public sealed record ResolvedParam(string Key, object? Value);

public interface IOrderTemplateResolver
{
    // Pull every mission's vendor params into the result. ACT-by-reference
    // entries are expanded by looking up the named ActionTemplate from the
    // catalog; the parameters are emitted with native JSON types so RIOT3
    // gets ints for id/param0/param1 and strings for param_str.
    Task<ResolvedOrder> ResolveAsync(OrderTemplate template, CancellationToken cancellationToken = default);
}
