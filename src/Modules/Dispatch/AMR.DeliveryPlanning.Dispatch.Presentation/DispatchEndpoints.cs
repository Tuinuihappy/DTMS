using AMR.DeliveryPlanning.Dispatch.Application.Commands.CancelTrip;
using AMR.DeliveryPlanning.Dispatch.Application.Commands.CapturePoD;
using AMR.DeliveryPlanning.Dispatch.Application.Commands.PauseTrip;
using AMR.DeliveryPlanning.Dispatch.Application.Commands.RaiseException;
using AMR.DeliveryPlanning.Dispatch.Application.Commands.ResolveException;
using AMR.DeliveryPlanning.Dispatch.Application.Commands.ResumeTrip;
using AMR.DeliveryPlanning.Dispatch.Application.Queries.GetTripById;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AMR.DeliveryPlanning.Dispatch.Presentation;

public static class DispatchEndpoints
{
    public static void MapDispatchEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/dispatch").WithTags("Dispatch").RequireAuthorization();

        // GET /api/v1/dispatch/trips/{id} — Get trip details
        group.MapGet("/trips/{id:guid}", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new GetTripByIdQuery(id));
            return result.IsSuccess ? Results.Ok(result.Value) : Results.NotFound(result.Error);
        });

        // GET /api/v1/dispatch/vehicles/{vehicleId}/trips — Get active trips for a vehicle
        group.MapGet("/vehicles/{vehicleId:guid}/trips", async (Guid vehicleId, ISender sender) =>
        {
            var result = await sender.Send(new GetActiveTripsByVehicleQuery(vehicleId));
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        });

        // POST /api/v1/dispatch/trips/{id}/pause
        group.MapPost("/trips/{id:guid}/pause", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new PauseTripCommand(id));
            return result.IsSuccess ? Results.Ok() : Results.BadRequest(result.Error);
        });

        // POST /api/v1/dispatch/trips/{id}/resume
        group.MapPost("/trips/{id:guid}/resume", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new ResumeTripCommand(id));
            return result.IsSuccess ? Results.Ok() : Results.BadRequest(result.Error);
        });

        // POST /api/v1/dispatch/trips/{id}/cancel
        group.MapPost("/trips/{id:guid}/cancel", async (Guid id, string reason, ISender sender) =>
        {
            var result = await sender.Send(new CancelTripCommand(id, reason));
            return result.IsSuccess ? Results.Ok() : Results.BadRequest(result.Error);
        });

        // POST /api/v1/dispatch/trips/{id}/exceptions — Raise an exception
        group.MapPost("/trips/{id:guid}/exceptions", async (Guid id, RaiseExceptionRequest req, ISender sender) =>
        {
            var result = await sender.Send(new RaiseExceptionCommand(id, req.Code, req.Severity, req.Detail));
            return result.IsSuccess
                ? Results.Created($"/api/v1/dispatch/trips/{id}/exceptions/{result.Value}", result.Value)
                : Results.BadRequest(result.Error);
        });

        // POST /api/v1/dispatch/trips/{tripId}/exceptions/{exceptionId}/resolve
        group.MapPost("/trips/{tripId:guid}/exceptions/{exceptionId:guid}/resolve",
            async (Guid tripId, Guid exceptionId, ResolveExceptionRequest req, ISender sender) =>
            {
                var result = await sender.Send(new ResolveExceptionCommand(tripId, exceptionId, req.Resolution, req.ResolvedBy));
                return result.IsSuccess ? Results.Ok() : Results.BadRequest(result.Error);
            });

        // POST /api/v1/dispatch/trips/{id}/pod — Capture Proof of Delivery
        group.MapPost("/trips/{id:guid}/pod", async (Guid id, CapturePodRequest req, ISender sender) =>
        {
            var result = await sender.Send(new CapturePodCommand(id, req.StopId, req.PhotoUrl, req.SignatureData, req.ScannedIds, req.Notes));
            return result.IsSuccess
                ? Results.Created($"/api/v1/dispatch/trips/{id}/pod/{result.Value}", result.Value)
                : Results.BadRequest(result.Error);
        });
    }
}

public record RaiseExceptionRequest(string Code, string Severity, string Detail);
public record ResolveExceptionRequest(string Resolution, string ResolvedBy);
public record CapturePodRequest(Guid StopId, string? PhotoUrl, string? SignatureData, List<string>? ScannedIds, string? Notes);
