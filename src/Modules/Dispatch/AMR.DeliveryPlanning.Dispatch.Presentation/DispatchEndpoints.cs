using AMR.DeliveryPlanning.Dispatch.Application.Commands.CancelTrip;
using AMR.DeliveryPlanning.Dispatch.Application.Commands.CapturePoD;
using AMR.DeliveryPlanning.Dispatch.Application.Commands.DispatchTrip;
using AMR.DeliveryPlanning.Dispatch.Application.Commands.PauseTrip;
using AMR.DeliveryPlanning.Dispatch.Application.Commands.RaiseException;
using AMR.DeliveryPlanning.Dispatch.Application.Commands.ReassignTrip;
using AMR.DeliveryPlanning.Dispatch.Application.Commands.ReportTaskCompleted;
using AMR.DeliveryPlanning.Dispatch.Application.Commands.ReportTaskFailed;
using AMR.DeliveryPlanning.Dispatch.Application.Commands.ResolveException;
using AMR.DeliveryPlanning.Dispatch.Application.Commands.ResumeTrip;
using AMR.DeliveryPlanning.Dispatch.Application.Commands.StartTrip;
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
        var group = app.MapGroup("/api/dispatch").WithTags("Dispatch").RequireAuthorization();

        // POST /api/dispatch/trips — Dispatch a new trip from a committed job
        group.MapPost("/trips", async (DispatchTripCommand command, ISender sender) =>
        {
            var result = await sender.Send(command);
            return result.IsSuccess
                ? Results.Created($"/api/dispatch/trips/{result.Value}", result.Value)
                : Results.BadRequest(result.Error);
        });

        // POST /api/dispatch/trips/{id}/start — Start a created trip
        group.MapPost("/trips/{id:guid}/start", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new StartTripCommand(id));
            return result.IsSuccess ? Results.Ok() : Results.BadRequest(result.Error);
        });

        // POST /api/dispatch/trips/{tripId}/tasks/{taskId}/complete — Report task completed
        group.MapPost("/trips/{tripId:guid}/tasks/{taskId:guid}/complete", async (Guid tripId, Guid taskId, ISender sender) =>
        {
            var result = await sender.Send(new ReportTaskCompletedCommand(tripId, taskId));
            return result.IsSuccess ? Results.Ok() : Results.BadRequest(result.Error);
        });

        // POST /api/dispatch/trips/{tripId}/tasks/{taskId}/fail — Report task failed
        group.MapPost("/trips/{tripId:guid}/tasks/{taskId:guid}/fail", async (Guid tripId, Guid taskId, string reason, ISender sender) =>
        {
            var result = await sender.Send(new ReportTaskFailedCommand(tripId, taskId, reason));
            return result.IsSuccess ? Results.Ok() : Results.BadRequest(result.Error);
        });

        // GET /api/dispatch/trips/{id} — Get trip details
        group.MapGet("/trips/{id:guid}", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new GetTripByIdQuery(id));
            return result.IsSuccess ? Results.Ok(result.Value) : Results.NotFound(result.Error);
        });

        // GET /api/dispatch/vehicles/{vehicleId}/trips — Get active trips for a vehicle
        group.MapGet("/vehicles/{vehicleId:guid}/trips", async (Guid vehicleId, ISender sender) =>
        {
            var result = await sender.Send(new GetActiveTripsByVehicleQuery(vehicleId));
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        });

        // POST /api/dispatch/trips/{id}/pause
        group.MapPost("/trips/{id:guid}/pause", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new PauseTripCommand(id));
            return result.IsSuccess ? Results.Ok() : Results.BadRequest(result.Error);
        });

        // POST /api/dispatch/trips/{id}/resume
        group.MapPost("/trips/{id:guid}/resume", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new ResumeTripCommand(id));
            return result.IsSuccess ? Results.Ok() : Results.BadRequest(result.Error);
        });

        // POST /api/dispatch/trips/{id}/cancel
        group.MapPost("/trips/{id:guid}/cancel", async (Guid id, string reason, ISender sender) =>
        {
            var result = await sender.Send(new CancelTripCommand(id, reason));
            return result.IsSuccess ? Results.Ok() : Results.BadRequest(result.Error);
        });

        // POST /api/dispatch/trips/{id}/reassign
        group.MapPost("/trips/{id:guid}/reassign", async (Guid id, ReassignTripCommand command, ISender sender) =>
        {
            var result = await sender.Send(command with { TripId = id });
            return result.IsSuccess ? Results.Ok() : Results.BadRequest(result.Error);
        });

        // POST /api/dispatch/trips/{id}/exceptions — Raise an exception
        group.MapPost("/trips/{id:guid}/exceptions", async (Guid id, RaiseExceptionRequest req, ISender sender) =>
        {
            var result = await sender.Send(new RaiseExceptionCommand(id, req.Code, req.Severity, req.Detail));
            return result.IsSuccess
                ? Results.Created($"/api/dispatch/trips/{id}/exceptions/{result.Value}", result.Value)
                : Results.BadRequest(result.Error);
        });

        // POST /api/dispatch/trips/{tripId}/exceptions/{exceptionId}/resolve
        group.MapPost("/trips/{tripId:guid}/exceptions/{exceptionId:guid}/resolve",
            async (Guid tripId, Guid exceptionId, ResolveExceptionRequest req, ISender sender) =>
            {
                var result = await sender.Send(new ResolveExceptionCommand(tripId, exceptionId, req.Resolution, req.ResolvedBy));
                return result.IsSuccess ? Results.Ok() : Results.BadRequest(result.Error);
            });

        // POST /api/dispatch/trips/{id}/pod — Capture Proof of Delivery
        group.MapPost("/trips/{id:guid}/pod", async (Guid id, CapturePodRequest req, ISender sender) =>
        {
            var result = await sender.Send(new CapturePodCommand(id, req.StopId, req.PhotoUrl, req.SignatureData, req.ScannedIds, req.Notes));
            return result.IsSuccess
                ? Results.Created($"/api/dispatch/trips/{id}/pod/{result.Value}", result.Value)
                : Results.BadRequest(result.Error);
        });
    }
}

public record RaiseExceptionRequest(string Code, string Severity, string Detail);
public record ResolveExceptionRequest(string Resolution, string ResolvedBy);
public record CapturePodRequest(Guid StopId, string? PhotoUrl, string? SignatureData, List<string>? ScannedIds, string? Notes);
