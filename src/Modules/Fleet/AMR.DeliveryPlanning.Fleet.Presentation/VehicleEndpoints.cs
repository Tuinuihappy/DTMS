using AMR.DeliveryPlanning.Fleet.Application.Commands.ChargingPolicy;
using AMR.DeliveryPlanning.Fleet.Application.Commands.Maintenance;
using AMR.DeliveryPlanning.Fleet.Application.Commands.RegisterVehicle;
using AMR.DeliveryPlanning.Fleet.Application.Commands.UpdateVehicleState;
using AMR.DeliveryPlanning.Fleet.Application.Commands.VehicleGroup;
using AMR.DeliveryPlanning.Fleet.Application.Commands.VehicleType;
using AMR.DeliveryPlanning.Fleet.Application.Queries.GetAvailableVehicles;
using AMR.DeliveryPlanning.Fleet.Application.Queries.GetFleetKpi;
using AMR.DeliveryPlanning.Fleet.Domain.Entities;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AMR.DeliveryPlanning.Fleet.Presentation;

public static class VehicleEndpoints
{
    public static void MapFleetEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/fleet").WithTags("Fleet").RequireAuthorization();

        group.MapPost("/vehicle-types", async (CreateVehicleTypeCommand command, ISender sender) =>
        {
            var result = await sender.Send(command);
            return result.IsSuccess
                ? Results.Created($"/api/fleet/vehicle-types/{result.Value}", result.Value)
                : Results.BadRequest(result.Error);
        });

        // ── Vehicles ───────────────────────────────────────────────────────
        group.MapPost("/vehicles", async (RegisterVehicleCommand command, ISender sender) =>
        {
            var result = await sender.Send(command);
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        });

        group.MapPut("/vehicles/{id:guid}/state", async (Guid id, UpdateVehicleStateCommand command, ISender sender) =>
        {
            if (id != command.VehicleId) return Results.BadRequest("ID mismatch");
            var result = await sender.Send(command);
            return result.IsSuccess ? Results.Ok() : Results.BadRequest(result.Error);
        });

        group.MapGet("/vehicles/available", async (ISender sender) =>
        {
            var result = await sender.Send(new GetAvailableVehiclesQuery());
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        });

        // ── Fleet KPI ──────────────────────────────────────────────────────
        group.MapGet("/kpi", async (ISender sender) =>
        {
            var result = await sender.Send(new GetFleetKpiQuery());
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        });

        // ── Charging Policies ──────────────────────────────────────────────
        group.MapPost("/charging-policies", async (UpsertChargingPolicyCommand command, ISender sender) =>
        {
            var result = await sender.Send(command);
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        });

        // ── Maintenance ────────────────────────────────────────────────────
        group.MapPost("/vehicles/{vehicleId:guid}/maintenance",
            async (Guid vehicleId, CreateMaintenanceRequest req, ISender sender) =>
            {
                var result = await sender.Send(new CreateMaintenanceRecordCommand(
                    vehicleId, req.Type, req.Reason, req.Technician, req.ScheduledAt));
                return result.IsSuccess
                    ? Results.Created($"/api/fleet/vehicles/{vehicleId}/maintenance/{result.Value}", result.Value)
                    : Results.BadRequest(result.Error);
            });

        group.MapPost("/vehicles/{vehicleId:guid}/maintenance/{recordId:guid}/complete",
            async (Guid vehicleId, Guid recordId, CompleteMaintenanceRequest req, ISender sender) =>
            {
                var result = await sender.Send(new CompleteMaintenanceCommand(vehicleId, recordId, req.Outcome));
                return result.IsSuccess ? Results.Ok() : Results.BadRequest(result.Error);
            });

        // ── Vehicle Groups ─────────────────────────────────────────────────
        group.MapPost("/groups", async (CreateVehicleGroupCommand command, ISender sender) =>
        {
            var result = await sender.Send(command);
            return result.IsSuccess
                ? Results.Created($"/api/fleet/groups/{result.Value}", result.Value)
                : Results.BadRequest(result.Error);
        });

        group.MapPost("/groups/{groupId:guid}/vehicles/{vehicleId:guid}",
            async (Guid groupId, Guid vehicleId, ISender sender) =>
            {
                var result = await sender.Send(new AddVehicleToGroupCommand(groupId, vehicleId));
                return result.IsSuccess ? Results.Ok() : Results.BadRequest(result.Error);
            });

        group.MapDelete("/groups/{groupId:guid}/vehicles/{vehicleId:guid}",
            async (Guid groupId, Guid vehicleId, ISender sender) =>
            {
                var result = await sender.Send(new RemoveVehicleFromGroupCommand(groupId, vehicleId));
                return result.IsSuccess ? Results.Ok() : Results.BadRequest(result.Error);
            });
    }
}

public record CreateMaintenanceRequest(MaintenanceType Type, string Reason, string? Technician, DateTime ScheduledAt);
public record CompleteMaintenanceRequest(string Outcome);
