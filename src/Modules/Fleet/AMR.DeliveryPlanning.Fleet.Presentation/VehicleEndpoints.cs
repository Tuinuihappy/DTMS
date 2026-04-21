using AMR.DeliveryPlanning.Fleet.Application.Commands.RegisterVehicle;
using AMR.DeliveryPlanning.Fleet.Application.Commands.UpdateVehicleState;
using AMR.DeliveryPlanning.Fleet.Application.Queries.GetAvailableVehicles;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AMR.DeliveryPlanning.Fleet.Presentation;

public static class VehicleEndpoints
{
    public static void MapFleetEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/fleet/vehicles").WithTags("Fleet");

        group.MapPost("/", async (RegisterVehicleCommand command, ISender sender) =>
        {
            var result = await sender.Send(command);
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        });

        group.MapPut("/{id:guid}/state", async (Guid id, UpdateVehicleStateCommand command, ISender sender) =>
        {
            if (id != command.VehicleId) return Results.BadRequest("ID mismatch");
            
            var result = await sender.Send(command);
            return result.IsSuccess ? Results.Ok() : Results.BadRequest(result.Error);
        });

        group.MapGet("/available", async (ISender sender) =>
        {
            var result = await sender.Send(new GetAvailableVehiclesQuery());
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        });
    }
}
