using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Facility.Application.Queries.GetWarehouseById;

/// <summary>
/// Full warehouse detail — includes the geofence WKT, full address,
/// contact email, and per-day operating hours that the list view omits
/// for payload size. Frontend uses this for edit pages + Phase 4
/// geofence editor.
/// </summary>
public sealed record WarehouseDetailDto(
    Guid Id,
    string Code,
    string Name,
    double Lat,
    double Lng,
    string AddressStreet,
    string? AddressCity,
    string? AddressState,
    string? AddressPostalCode,
    string? AddressCountry,
    string[] ServiceModes,
    int? GeofenceRadiusM,
    string? GeofenceAreaWkt,
    string? ContactName,
    string? ContactPhone,
    string? ContactEmail,
    // Operating hours per day-of-week (TimeOnly serialized as HH:mm strings
    // for JSON friendliness — null both = closed that day).
    OperatingHoursDayDto Monday,
    OperatingHoursDayDto Tuesday,
    OperatingHoursDayDto Wednesday,
    OperatingHoursDayDto Thursday,
    OperatingHoursDayDto Friday,
    OperatingHoursDayDto Saturday,
    OperatingHoursDayDto Sunday,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public sealed record OperatingHoursDayDto(string? Open, string? Close);

public sealed record GetWarehouseByIdQuery(Guid Id) : IQuery<WarehouseDetailDto>;
