using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using DTMS.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Facility.Application.Queries.GetWarehouses;

/// <summary>
/// Flat DTO for warehouse list responses. Mirrors the aggregate shape
/// but with value objects flattened into primitive properties — keeps
/// the JSON simple for frontend pickers (warehouse-combobox in Phase 2.7b).
/// </summary>
public sealed record WarehouseListItemDto(
    Guid Id,
    string Code,
    string Name,
    double Lat,
    double Lng,
    string AddressStreet,
    string? AddressCity,
    string[] ServiceModes,
    int? GeofenceRadiusM,
    bool HasGeofencePolygon,
    string? ContactName,
    string? ContactPhone,
    bool IsActive,
    DateTime CreatedAt);

/// <summary>
/// List warehouses with optional service-mode filter. Frontend picker
/// passes ?serviceMode=Amr to only show warehouses that serve AMR
/// orders. ExcludeInactive=true by default (the common case for picker
/// UIs); admin pages can pass false to see soft-deleted history.
/// </summary>
public sealed record GetWarehousesQuery(
    TransportMode? ServiceMode = null,
    bool ExcludeInactive = true
) : IQuery<IReadOnlyList<WarehouseListItemDto>>;
