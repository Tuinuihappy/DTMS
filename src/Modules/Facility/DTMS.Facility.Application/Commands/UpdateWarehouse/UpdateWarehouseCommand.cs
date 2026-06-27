using DTMS.DeliveryOrder.Domain.Enums;
using DTMS.SharedKernel.Messaging;

namespace DTMS.Facility.Application.Commands.UpdateWarehouse;

/// <summary>
/// Partial-update for a Warehouse. Every field is nullable — null means
/// "don't change". This is the API surface for the admin edit form;
/// Phase 4 Manual mode hooks into the geofence portion specifically.
///
/// Code is NOT updatable from this command — codes are stable identifiers
/// used in URLs / external references. If a code needs to change,
/// deactivate the old warehouse and create a new one.
///
/// ServiceModes uses null=skip vs empty-array=invalid-state, matching
/// the domain invariant that ≥1 mode is always required.
/// </summary>
public sealed record UpdateWarehouseCommand(
    Guid Id,
    string? Name = null,
    // Location: pass BOTH or NEITHER (an Address without coords is meaningless
    // for geofence/maps).
    double? Lat = null,
    double? Lng = null,
    string? AddressStreet = null,
    string? AddressCity = null,
    string? AddressState = null,
    string? AddressPostalCode = null,
    string? AddressCountry = null,
    IReadOnlyList<TransportMode>? ServiceModes = null,
    // Contact: pass all three fields to set; ContactName="" + ContactPhone=""
    // clears the contact.
    string? ContactName = null,
    string? ContactPhone = null,
    string? ContactEmail = null,
    // Geofence: only one of these makes sense at a time; passing both
    // returns an error. Sentinel int.MinValue / "__CLEAR__" string indicates
    // explicit clear (so null = skip vs sentinel = clear).
    int? GeofenceRadiusM = null,
    string? GeofenceAreaWkt = null,
    bool ClearGeofence = false
) : ICommand;
