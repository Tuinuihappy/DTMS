using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using DTMS.Facility.Application.Commands.CreateWarehouse;
using DTMS.Facility.Application.Commands.UpdateWarehouse;
using DTMS.Facility.Application.Commands.WarehouseLifecycle;
using DTMS.Facility.Application.Queries.GetWarehouseById;
using DTMS.Facility.Application.Queries.GetWarehouses;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DTMS.Facility.Presentation;

/// <summary>
/// Warehouse CRUD endpoints (Phase 2.7a). Kept in a separate file from
/// MapEndpoints.cs because they're a distinct surface — frontend
/// warehouse-combobox (Phase 2.7b) talks to these, dispatcher console
/// admin pages (Phase 4-prep) talk to these, and the existing Map/Station
/// endpoints stay AMR-specific.
///
/// Conventions match MapEndpoints (mounted under /api/v1/facility, JWT
/// required, ISender for command dispatch, Result&lt;T&gt; → Ok/BadRequest).
/// </summary>
public static class WarehouseEndpoints
{
    public static void MapWarehouseEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/facility/warehouses")
            .WithTags("Warehouses")
            .RequireAuthorization();

        // POST /api/v1/facility/warehouses
        // → 201 Created with Id, or 400 with error message
        group.MapPost("/", async (CreateWarehouseRequest req, ISender sender) =>
        {
            var command = new CreateWarehouseCommand(
                Code: req.Code,
                Name: req.Name,
                Lat: req.Lat,
                Lng: req.Lng,
                AddressStreet: req.AddressStreet,
                AddressCity: req.AddressCity,
                AddressState: req.AddressState,
                AddressPostalCode: req.AddressPostalCode,
                AddressCountry: req.AddressCountry,
                ServiceModes: req.ServiceModes?.Select(ParseMode).ToList(),
                ContactName: req.ContactName,
                ContactPhone: req.ContactPhone,
                ContactEmail: req.ContactEmail,
                GeofenceRadiusM: req.GeofenceRadiusM,
                GeofenceAreaWkt: req.GeofenceAreaWkt);

            var result = await sender.Send(command);
            return result.IsSuccess
                ? Results.Created($"/api/v1/facility/warehouses/{result.Value}", new { id = result.Value })
                : Results.BadRequest(new { error = result.Error });
        });

        // GET /api/v1/facility/warehouses?serviceMode=Amr&excludeInactive=true
        // The picker UI (Phase 2.7b) calls this on every dropdown open;
        // the response is shallow enough to fit ~100 warehouses without
        // pagination — past that we add ?page= + ?size=.
        // excludeInactive defaults true — picker UI omits the query
        // param entirely when filtering active-only (the common case);
        // making it required would 500 every dropdown open.
        group.MapGet("/", async (string? serviceMode, bool? excludeInactive, ISender sender) =>
        {
            TransportMode? mode = !string.IsNullOrWhiteSpace(serviceMode)
                ? ParseMode(serviceMode)
                : null;
            var result = await sender.Send(new GetWarehousesQuery(mode, excludeInactive ?? true));
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(new { error = result.Error });
        });

        // GET /api/v1/facility/warehouses/{id}
        // Full detail — used by edit form and Phase 4 geofence editor.
        group.MapGet("/{id:guid}", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new GetWarehouseByIdQuery(id));
            return result.IsSuccess ? Results.Ok(result.Value) : Results.NotFound(new { error = result.Error });
        });

        // PATCH /api/v1/facility/warehouses/{id}
        // Partial update — every field optional except Id. Frontend sends
        // only the fields the operator changed.
        group.MapMethods("/{id:guid}", ["PATCH"], async (Guid id, UpdateWarehouseRequest req, ISender sender) =>
        {
            var command = new UpdateWarehouseCommand(
                Id: id,
                Name: req.Name,
                Lat: req.Lat,
                Lng: req.Lng,
                AddressStreet: req.AddressStreet,
                AddressCity: req.AddressCity,
                AddressState: req.AddressState,
                AddressPostalCode: req.AddressPostalCode,
                AddressCountry: req.AddressCountry,
                ServiceModes: req.ServiceModes?.Select(ParseMode).ToList(),
                ContactName: req.ContactName,
                ContactPhone: req.ContactPhone,
                ContactEmail: req.ContactEmail,
                GeofenceRadiusM: req.GeofenceRadiusM,
                GeofenceAreaWkt: req.GeofenceAreaWkt,
                ClearGeofence: req.ClearGeofence);

            var result = await sender.Send(command);
            return result.IsSuccess ? Results.NoContent() : Results.BadRequest(new { error = result.Error });
        });

        // POST /api/v1/facility/warehouses/{id}/deactivate
        // Soft-delete — picker UIs hide it but in-flight trips still resolve.
        group.MapPost("/{id:guid}/deactivate", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new DeactivateWarehouseCommand(id));
            return result.IsSuccess ? Results.NoContent() : Results.BadRequest(new { error = result.Error });
        });

        // POST /api/v1/facility/warehouses/{id}/reactivate
        group.MapPost("/{id:guid}/reactivate", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new ReactivateWarehouseCommand(id));
            return result.IsSuccess ? Results.NoContent() : Results.BadRequest(new { error = result.Error });
        });
    }

    // Case-insensitive enum parse — operator UIs sometimes lowercase
    // ("amr" / "manual") for URL friendliness. Throws on bad input which
    // surfaces as 400 — fine because the picker only sends valid values.
    private static TransportMode ParseMode(string raw) =>
        Enum.Parse<TransportMode>(raw, ignoreCase: true);
}

// ── Request DTOs ─────────────────────────────────────────────────────────
// Separate from the command records because (1) HTTP shape may diverge
// from the application command over time, (2) ASP.NET model binding
// works better with regular classes than nested record types with
// transport-mode enums.

public sealed record CreateWarehouseRequest(
    string Code,
    string Name,
    double Lat,
    double Lng,
    string AddressStreet,
    string? AddressCity = null,
    string? AddressState = null,
    string? AddressPostalCode = null,
    string? AddressCountry = null,
    IReadOnlyList<string>? ServiceModes = null,
    string? ContactName = null,
    string? ContactPhone = null,
    string? ContactEmail = null,
    int? GeofenceRadiusM = null,
    string? GeofenceAreaWkt = null);

public sealed record UpdateWarehouseRequest(
    string? Name = null,
    double? Lat = null,
    double? Lng = null,
    string? AddressStreet = null,
    string? AddressCity = null,
    string? AddressState = null,
    string? AddressPostalCode = null,
    string? AddressCountry = null,
    IReadOnlyList<string>? ServiceModes = null,
    string? ContactName = null,
    string? ContactPhone = null,
    string? ContactEmail = null,
    int? GeofenceRadiusM = null,
    string? GeofenceAreaWkt = null,
    bool ClearGeofence = false);
