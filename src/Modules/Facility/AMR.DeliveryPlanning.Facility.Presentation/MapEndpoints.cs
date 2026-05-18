using AMR.DeliveryPlanning.Facility.Application.Commands.AddStation;
using AMR.DeliveryPlanning.Facility.Application.Commands.CreateMap;
using AMR.DeliveryPlanning.Facility.Application.Commands.ImportMapFromRiot3;
using AMR.DeliveryPlanning.Facility.Application.Commands.UpdateStation;
using AMR.DeliveryPlanning.Facility.Application.Commands.FacilityResource;
using AMR.DeliveryPlanning.Facility.Application.Commands.RegisterCarrierTypeProfile;
using AMR.DeliveryPlanning.Facility.Application.Commands.ReleaseShelf;
using AMR.DeliveryPlanning.Facility.Application.Commands.RegisterLoadUnitProfile;
using AMR.DeliveryPlanning.Facility.Application.Commands.TopologyOverlay;
using AMR.DeliveryPlanning.Facility.Application.Queries.GetCarrierTypeProfiles;
using AMR.DeliveryPlanning.Facility.Application.Queries.GetLoadUnitProfiles;
using AMR.DeliveryPlanning.Facility.Application.Queries.GetMapById;
using AMR.DeliveryPlanning.Facility.Application.Queries.GetRouteCost;
using AMR.DeliveryPlanning.Facility.Application.Queries.GetStations;
using AMR.DeliveryPlanning.Facility.Domain.Entities;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AMR.DeliveryPlanning.Facility.Presentation;

public static class MapEndpoints
{
    public static void MapFacilityEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/facility").WithTags("Facility").RequireAuthorization();

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

        group.MapPost("/maps/import-from-riot3", async (ImportMapFromRiot3Request req, ISender sender) =>
        {
            var result = await sender.Send(new ImportMapFromRiot3Command(req.Riot3MapId));
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        });

        // ── Stations ───────────────────────────────────────────────────────
        // POST /api/facility/maps/{mapId}/stations
        group.MapPost("/maps/{mapId:guid}/stations", async (Guid mapId, AddStationRequest req, ISender sender) =>
        {
            var result = await sender.Send(new AddStationCommand(
                mapId, req.Name, req.X, req.Y, req.Theta, req.Type, req.VendorRef, req.Code));
            return result.IsSuccess
                ? Results.Created($"/api/facility/stations/{result.Value}", result.Value)
                : Results.BadRequest(result.Error);
        });

        // GET /api/facility/stations?mapId=&type=&zoneId=&compatibleWith=&includeInactive=
        group.MapGet("/stations", async (Guid? mapId, string? type, Guid? zoneId, string? compatibleWith, bool includeInactive, ISender sender) =>
        {
            StationType? stationType = type != null && Enum.TryParse<StationType>(type, true, out var t) ? t : null;
            var result = await sender.Send(new GetStationsQuery(mapId, stationType, zoneId, compatibleWith, includeInactive));
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        });

        // PATCH /api/facility/stations/{stationId}
        group.MapMethods("/stations/{stationId:guid}", ["PATCH"],
            async (Guid stationId, UpdateStationRequest req, ISender sender) =>
            {
                var result = await sender.Send(new UpdateStationCommand(stationId, req.Type, req.Code));
                return result.IsSuccess ? Results.NoContent() : Results.BadRequest(result.Error);
            });

        // ── Route Costs ────────────────────────────────────────────────────
        // GET /api/facility/route-cost?from=&to=
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
                ? Results.Created($"/api/facility/topology-overlays/{result.Value}", result.Value)
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
                ? Results.Created($"/api/facility/carrier-type-profiles/{result.Value}", result.Value)
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
                ? Results.Created($"/api/facility/load-unit-profiles/{result.Value}", result.Value)
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
                ? Results.Created($"/api/facility/resources/{result.Value}", result.Value)
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
public record UpdateStationRequest(StationType? Type, string? Code);
public record AddStationRequest(string Name, double X, double Y, double? Theta, StationType Type = StationType.Normal,
    string? VendorRef = null, string? Code = null);
