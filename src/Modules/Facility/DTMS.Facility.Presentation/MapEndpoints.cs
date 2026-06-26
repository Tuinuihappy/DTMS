using DTMS.Facility.Application.Commands.AddStation;
using DTMS.Facility.Application.Commands.ClearStationOverride;
using DTMS.Facility.Application.Commands.CreateMap;
using DTMS.Facility.Application.Commands.ForceStationOffline;
using DTMS.Facility.Application.Commands.ImportMapFromRiot3;
using DTMS.Facility.Application.Commands.UpdateStation;
using DTMS.Facility.Application.Commands.FacilityResource;
using DTMS.Facility.Application.Commands.RegisterCarrierTypeProfile;
using DTMS.Facility.Application.Commands.ReleaseShelf;
using DTMS.Facility.Application.Commands.RegisterLoadUnitProfile;
using DTMS.Facility.Application.Commands.SyncMapStations;
using DTMS.Facility.Application.Commands.TopologyOverlay;
using DTMS.Facility.Application.Queries.GetCarrierTypeProfiles;
using DTMS.Facility.Application.Queries.GetLoadUnitProfiles;
using DTMS.Facility.Application.Queries.GetMapById;
using DTMS.Facility.Application.Queries.GetRouteCost;
using DTMS.Facility.Application.Queries.GetStations;
using DTMS.Facility.Application.Queries.ListMaps;
using DTMS.Facility.Domain.Entities;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DTMS.Facility.Presentation;

public static class MapEndpoints
{
    public static void MapFacilityEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/facility").WithTags("Facility").RequireAuthorization();

        // ── Maps ───────────────────────────────────────────────────────────
        group.MapPost("/maps", async (CreateMapCommand command, ISender sender) =>
        {
            var result = await sender.Send(command);
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        });

        group.MapGet("/maps/{id:guid}", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new GetMapByIdQuery(id));
            return result.IsSuccess ? Results.Ok(result.Value) : Results.NotFound(result.Error);
        });

        group.MapGet("/maps", async (ISender sender) =>
        {
            var result = await sender.Send(new ListMapsQuery());
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        });

        group.MapPost("/maps/import-from-riot3", async (ImportMapFromRiot3Request req, ISender sender) =>
        {
            var result = await sender.Send(new ImportMapFromRiot3Command(req.Riot3MapId));
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        });

        // Manual on-demand sync — same code path as the background poller, so the user
        // can refresh a map immediately after editing it in RIOT3 instead of waiting
        // for the next poll interval (default 30 min).
        group.MapPost("/maps/{id:guid}/sync-stations", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new SyncMapStationsCommand(id));
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        });

        // ── Stations ───────────────────────────────────────────────────────
        // POST /api/v1/facility/maps/{mapId}/stations
        group.MapPost("/maps/{mapId:guid}/stations", async (Guid mapId, AddStationRequest req, ISender sender) =>
        {
            var result = await sender.Send(new AddStationCommand(
                mapId, req.Name, req.X, req.Y, req.Theta, req.Type, req.VendorRef, req.Code,
                req.Actions));
            return result.IsSuccess
                ? Results.Created($"/api/v1/facility/stations/{result.Value}", result.Value)
                : Results.BadRequest(result.Error);
        });

        // GET /api/v1/facility/stations?mapId=&type=&zoneId=&compatibleWith=&includeInactive=&code=
        group.MapGet("/stations", async (Guid? mapId, string? type, Guid? zoneId, string? compatibleWith, bool includeInactive, string? code, ISender sender) =>
        {
            StationType? stationType = type != null && Enum.TryParse<StationType>(type, true, out var t) ? t : null;
            var result = await sender.Send(new GetStationsQuery(mapId, stationType, zoneId, compatibleWith, includeInactive, code));
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        });

        // PATCH /api/v1/facility/stations/{stationId}
        group.MapMethods("/stations/{stationId:guid}", ["PATCH"],
            async (Guid stationId, UpdateStationRequest req, ISender sender) =>
            {
                var result = await sender.Send(new UpdateStationCommand(
                    stationId, req.Type, req.Code,
                    req.UpdateActions, req.Actions));
                return result.IsSuccess ? Results.NoContent() : Results.BadRequest(result.Error);
            });

        // POST /api/v1/facility/stations/{stationId}/force-offline
        // Manual operator override — TTL-bounded (5..1440 minutes). Survives RIOT3 sync until cleared or expired.
        group.MapPost("/stations/{stationId:guid}/force-offline",
            async (Guid stationId, ForceOfflineRequest req, ISender sender) =>
            {
                var result = await sender.Send(new ForceStationOfflineCommand(
                    stationId, req.Reason, req.DurationMinutes, req.By));
                return result.IsSuccess ? Results.NoContent() : Results.BadRequest(result.Error);
            });

        // DELETE /api/v1/facility/stations/{stationId}/force-offline
        group.MapDelete("/stations/{stationId:guid}/force-offline",
            async (Guid stationId, ISender sender) =>
            {
                var result = await sender.Send(new ClearStationOverrideCommand(stationId));
                return result.IsSuccess ? Results.NoContent() : Results.BadRequest(result.Error);
            });

        // ── Route Costs ────────────────────────────────────────────────────
        // GET /api/v1/facility/route-cost?from=&to=
        group.MapGet("/route-cost", async (Guid from, Guid to, ISender sender) =>
        {
            var result = await sender.Send(new GetRouteCostQuery(from, to));
            return result.IsSuccess ? Results.Ok(result.Value) : Results.NotFound(result.Error);
        });

        // ── Topology Overlays ──────────────────────────────────────────────
        group.MapPost("/topology-overlays", async (CreateTopologyOverlayCommand command, ISender sender) =>
        {
            var result = await sender.Send(command);
            return result.IsSuccess
                ? Results.Created($"/api/v1/facility/topology-overlays/{result.Value}", result.Value)
                : Results.BadRequest(result.Error);
        });

        group.MapDelete("/topology-overlays/{id:guid}", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new ExpireTopologyOverlayCommand(id));
            return result.IsSuccess ? Results.Ok() : Results.BadRequest(result.Error);
        });

        // ── Shelves ────────────────────────────────────────────────────────
        group.MapPost("/shelves/{rfid}/release", async (string rfid, ISender sender) =>
        {
            var result = await sender.Send(new ReleaseShelfCommand(rfid));
            return result.IsSuccess ? Results.Ok() : Results.BadRequest(result.Error);
        });

        // ── Carrier Type Profiles ─────────────────────────────────────────
        group.MapPost("/carrier-type-profiles", async (RegisterCarrierTypeProfileCommand command, ISender sender) =>
        {
            var result = await sender.Send(command);
            return result.IsSuccess
                ? Results.Created($"/api/v1/facility/carrier-type-profiles/{result.Value}", result.Value)
                : Results.BadRequest(result.Error);
        });

        group.MapGet("/carrier-type-profiles", async (ISender sender) =>
        {
            var result = await sender.Send(new GetCarrierTypeProfilesQuery());
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        });

        // ── Load Unit Profiles ────────────────────────────────────────────
        group.MapPost("/load-unit-profiles", async (RegisterLoadUnitProfileCommand command, ISender sender) =>
        {
            var result = await sender.Send(command);
            return result.IsSuccess
                ? Results.Created($"/api/v1/facility/load-unit-profiles/{result.Value}", result.Value)
                : Results.BadRequest(result.Error);
        });

        group.MapGet("/load-unit-profiles", async (string? carrierTypeCode, ISender sender) =>
        {
            var result = await sender.Send(new GetLoadUnitProfilesQuery(carrierTypeCode));
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        });

        // ── Facility Resources ─────────────────────────────────────────────
        group.MapPost("/resources", async (RegisterFacilityResourceCommand command, ISender sender) =>
        {
            var result = await sender.Send(command);
            return result.IsSuccess
                ? Results.Created($"/api/v1/facility/resources/{result.Value}", result.Value)
                : Results.BadRequest(result.Error);
        });

        group.MapPost("/resources/{id:guid}/command", async (Guid id, ResourceCommandRequest req, ISender sender) =>
        {
            var result = await sender.Send(new CommandFacilityResourceCommand(id, req.Command));
            return result.IsSuccess ? Results.Ok() : Results.BadRequest(result.Error);
        });
    }
}

public record ResourceCommandRequest(string Command);
public record ImportMapFromRiot3Request(int Riot3MapId);
// UpdateActions=false (default) leaves the existing action map untouched;
// set UpdateActions=true and pass null/empty Actions to explicitly clear it,
// or a non-empty map to replace it wholesale.
public record UpdateStationRequest(
    StationType? Type,
    string? Code,
    bool UpdateActions = false,
    IDictionary<string, StationActionInput>? Actions = null);

public record ForceOfflineRequest(string Reason, int DurationMinutes, string? By = null);

public record AddStationRequest(
    string Name,
    double X,
    double Y,
    double? Theta,
    StationType Type = StationType.Normal,
    string? VendorRef = null,
    string? Code = null,
    // Optional action map keyed by intent ("lift", "drop", "charge", ...).
    IDictionary<string, StationActionInput>? Actions = null);
