using DTMS.SharedKernel.Messaging;

namespace DTMS.Wms.Application.Queries.GetWmsLocations;

/// <summary>
/// Read the local WMS snapshot. Serves the frontend picker and any admin
/// tooling that wants to inspect what's cached. Never touches the external
/// WMS — <see cref="Commands.SyncWmsLocations.SyncWmsLocationsCommand"/>
/// is the sole gatekeeper for that.
/// </summary>
public record GetWmsLocationsQuery(
    string? Search = null,
    string? ParentCode = null,
    int Page = 1,
    int PageSize = 20,
    bool IncludeInactive = false)
    : IQuery<GetWmsLocationsResult>;

public sealed record GetWmsLocationsResult(
    int Total,
    int Page,
    int PageSize,
    IReadOnlyList<WmsLocationSummaryDto> Data);

public sealed record WmsLocationSummaryDto(
    Guid Id,
    int ExternalId,
    string Code,
    string DisplayName,
    int Type,
    string? TypeName,
    bool IsActive,
    string? ParentLocationCode,
    string? ParentLocationDisplayName,
    double? Latitude,
    double? Longitude);
