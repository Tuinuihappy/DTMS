using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using DTMS.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Facility.Application.Commands.CreateWarehouse;

/// <summary>
/// Create a new Warehouse (per ADR-002 — Phase 2.1/2.6 plumbing).
///
/// The flat shape — instead of nested value-object DTOs — keeps the
/// HTTP body shallow for operators using the form UI. The handler
/// composes the LatLng / Address / ContactInfo / OperatingHours value
/// objects internally; consumers don't need to know domain structure.
///
/// Optional fields: PrimaryContact* (all three Name/Phone/Email),
/// GeofenceRadiusM OR GeofenceAreaWkt (mutually exclusive — domain
/// enforces). ServiceModes defaults to [Amr] if caller omits, matching
/// the historical assumption that every warehouse serves AMR.
/// </summary>
public sealed record CreateWarehouseCommand(
    string Code,
    string Name,
    double Lat,
    double Lng,
    string AddressStreet,
    string? AddressCity = null,
    string? AddressState = null,
    string? AddressPostalCode = null,
    string? AddressCountry = null,
    IReadOnlyList<TransportMode>? ServiceModes = null,
    string? ContactName = null,
    string? ContactPhone = null,
    string? ContactEmail = null,
    int? GeofenceRadiusM = null,
    string? GeofenceAreaWkt = null
) : ICommand<Guid>;
