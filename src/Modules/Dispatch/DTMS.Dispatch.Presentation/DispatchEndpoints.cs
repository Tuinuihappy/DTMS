using DTMS.Dispatch.Application.Commands.AcknowledgeRobotPass;
using DTMS.Dispatch.Application.Commands.BulkCancelTrips;
using DTMS.Dispatch.Application.Commands.CancelTrip;
using DTMS.Dispatch.Application.Commands.CapturePoD;
using DTMS.Dispatch.Application.Commands.PauseTrip;
using DTMS.Dispatch.Application.Commands.RaiseException;
using DTMS.Dispatch.Application.Commands.ReissueTrip;
using DTMS.Dispatch.Application.Commands.ResolveException;
using DTMS.Dispatch.Application.Commands.ResumeTrip;
using DTMS.Dispatch.Application.Projections;
using DTMS.Dispatch.Application.Queries.GetTripById;
using DTMS.Dispatch.Application.Queries.GetTripDetails;
using DTMS.Dispatch.Application.Queries.GetTripItems;
using DTMS.Dispatch.Application.Queries.GetTripRetryHistory;
using DTMS.Dispatch.Application.Queries.GetTripsByOrder;
using DTMS.Dispatch.Application.Queries.GetTripsQueue;
using DTMS.Dispatch.Application.Queries.GetTripStatusHistory;
using DTMS.Dispatch.Domain.Enums;
using DTMS.Iam.Application.Authorization;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DTMS.Dispatch.Presentation;

public static class DispatchEndpoints
{
    public static void MapDispatchEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/dispatch").WithTags("Dispatch").RequireAuthorization();

        // GET /api/v1/dispatch/trips?status=...&search=...&vehicleKey=...&fromUtc=...&toUtc=...&sortBy=...&sortDir=...&page=...&pageSize=...
        // — Paginated operator Trips list. `status` is a repeating query
        // param (e.g. ?status=Created&status=InProgress); empty = no
        // status filter. `search` matches UpperKey / VendorOrderKey /
        // OrderRef (case-insensitive substring). Returns
        // { items, totalCount, page, pageSize }.
        group.MapGet("/trips", async (HttpRequest req, ISender sender) =>
        {
            var rawStatuses = req.Query["status"];
            var statuses = new List<TripStatus>(rawStatuses.Count);
            foreach (var token in rawStatuses)
            {
                if (string.IsNullOrWhiteSpace(token)) continue;
                if (!Enum.TryParse<TripStatus>(token, ignoreCase: true, out var parsed))
                    return Results.BadRequest($"Unknown TripStatus '{token}'.");
                statuses.Add(parsed);
            }

            var search = req.Query["search"].ToString();
            var vehicleKey = req.Query["vehicleKey"].ToString();

            DateTime? fromUtc = DateTime.TryParse(req.Query["fromUtc"], null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var f) ? f : null;
            DateTime? toUtc = DateTime.TryParse(req.Query["toUtc"], null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var t) ? t : null;

            var sortByRaw = req.Query["sortBy"].ToString();
            if (!Enum.TryParse<TripQueueSort>(sortByRaw, ignoreCase: true, out var sortBy))
                sortBy = TripQueueSort.CreatedAt;
            var sortDirRaw = req.Query["sortDir"].ToString();
            var sortDesc = string.IsNullOrEmpty(sortDirRaw)
                ? true
                : !sortDirRaw.Equals("asc", StringComparison.OrdinalIgnoreCase);

            var page = int.TryParse(req.Query["page"], out var p) && p > 0 ? p : 1;
            var pageSize = int.TryParse(req.Query["pageSize"], out var ps) && ps > 0 ? ps : 20;

            var result = await sender.Send(new GetTripsQueueQuery(
                statuses, search, vehicleKey, fromUtc, toUtc, sortBy, sortDesc, page, pageSize));
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        }).RequirePermission("dtms:trip:read");

        // GET /api/v1/dispatch/trips/{id} — Get trip details
        group.MapGet("/trips/{id:guid}", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new GetTripByIdQuery(id));
            return result.IsSuccess ? Results.Ok(result.Value) : Results.NotFound(result.Error);
        }).RequirePermission("dtms:trip:read");

        // GET /api/v1/dispatch/trips/{id}/status-history — Phase P1 (b12)
        // Structured status-transition timeline from TripStatusHistoryProjector.
        // Same shape as Order/Job endpoints — operator drawer wires to all
        // three through one shared <StatusTimelineSection /> component.
        group.MapGet("/trips/{id:guid}/status-history", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new GetTripStatusHistoryQuery(id));
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        }).RequirePermission("dtms:trip:read");

        // GET /api/v1/dispatch/trips/{id}/details — Full operator view: trip
        // state + vendor snapshot fields + per-mission timeline. Append
        // ?includeRaw=true to also return the raw vendor JSON blobs
        // (compliance use only — they can be megabytes each).
        group.MapGet("/trips/{id:guid}/details", async (Guid id, bool? includeRaw, ISender sender) =>
        {
            var result = await sender.Send(new GetTripDetailsQuery(id, includeRaw ?? false));
            return result.IsSuccess ? Results.Ok(result.Value) : Results.NotFound(result.Error);
        }).RequirePermission("dtms:trip:read");

        // GET /api/v1/dispatch/trips/{id}/items — Phase P5.3 — items
        // bound to this trip plus each item's owning order context.
        // Backed by dispatch.TripItems (TripItemsProjector materializes
        // it from TripStartedIntegrationEvent.Items). Empty list when the
        // projector hasn't seen the trip yet (vendor adapter hasn't bound
        // items — operator should retry shortly).
        group.MapGet("/trips/{id:guid}/items", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new GetTripItemsQuery(id));
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        }).RequirePermission("dtms:trip:read");

        // GET /api/v1/dispatch/orders/{orderId}/trips — list every Trip of an
        // order (all attempts, sorted Attempt#, Created asc) so the operator
        // UI can drill from an order into its dispatch lineage.
        group.MapGet("/orders/{orderId:guid}/trips", async (Guid orderId, ISender sender) =>
        {
            var result = await sender.Send(new GetTripsByOrderQuery(orderId));
            return result.IsSuccess ? Results.Ok(result.Value) : Results.NotFound(result.Error);
        }).RequirePermission("dtms:trip:read");

        // GET /api/v1/dispatch/trips/{id}/retry-history — every attempt of the
        // same (Pickup, Drop) group on the same order, each tagged with the
        // TripRetryEvent that produced it. Backs the operator retry-chain
        // timeline in the Trip detail drawer.
        group.MapGet("/trips/{id:guid}/retry-history", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new GetTripRetryHistoryQuery(id));
            return result.IsSuccess ? Results.Ok(result.Value) : Results.NotFound(result.Error);
        }).RequirePermission("dtms:trip:read");

        // GET /api/v1/dispatch/vehicles/{vehicleId}/trips — Get active trips for a vehicle
        group.MapGet("/vehicles/{vehicleId:guid}/trips", async (Guid vehicleId, ISender sender) =>
        {
            var result = await sender.Send(new GetActiveTripsByVehicleQuery(vehicleId));
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        }).RequirePermission("dtms:trip:read");

        // POST /api/v1/dispatch/trips/{id}/pause
        group.MapPost("/trips/{id:guid}/pause", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new PauseTripCommand(id));
            return result.IsSuccess ? Results.Ok() : Results.BadRequest(result.Error);
        }).RequirePermission("dtms:trip:pause");

        // POST /api/v1/dispatch/trips/{id}/resume
        group.MapPost("/trips/{id:guid}/resume", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new ResumeTripCommand(id));
            return result.IsSuccess ? Results.Ok() : Results.BadRequest(result.Error);
        }).RequirePermission("dtms:trip:pause");

        // POST /api/v1/dispatch/trips/{id}/acknowledge-robot-pass — operator
        // confirms a robot waiting at a checkpoint may proceed (RIOT3 PASS).
        // Trip.Status is unchanged on success — see AcknowledgeRobotPass on
        // the Trip aggregate for the invariant.
        group.MapPost("/trips/{id:guid}/acknowledge-robot-pass", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new AcknowledgeRobotPassCommand(id));
            return result.IsSuccess ? Results.Ok() : Results.BadRequest(result.Error);
        }).RequirePermission("dtms:trip:acknowledge");

        // POST /api/v1/dispatch/trips/{id}/cancel
        group.MapPost("/trips/{id:guid}/cancel", async (Guid id, string reason, ISender sender) =>
        {
            var result = await sender.Send(new CancelTripCommand(id, reason));
            return result.IsSuccess ? Results.Ok() : Results.BadRequest(result.Error);
        }).RequirePermission("dtms:trip:cancel");

        // POST /api/v1/dispatch/trips/bulk-cancel — Backend Phase 2
        // Body: { tripIds: [guid...], reason: string }
        // Mirrors /api/v1/delivery-orders/bulk-cancel response semantics:
        // 200 on full success, 207 Multi-Status on partial, 400 if every
        // id failed. No idempotency middleware (matches single-cancel
        // endpoint behaviour); handler dedups ids inside the batch.
        group.MapPost("/trips/bulk-cancel", async (BulkCancelTripsCommand command, ISender sender) =>
        {
            var result = await sender.Send(command);
            if (result.IsFailure) return Results.BadRequest(result.Error);

            var bulk = result.Value;
            if (bulk.Succeeded.Count == 0)
                return Results.BadRequest(bulk.Failures);

            return bulk.Failures.Count > 0
                ? Results.Json(bulk, statusCode: 207)
                : Results.Ok(bulk);
        }).RequirePermission("dtms:trip:cancel");

        // POST /api/v1/dispatch/trips/{id}/retry — reissue a Cancelled trip.
        // Failed trips reject with a hint to reopen the order first.
        group.MapPost("/trips/{id:guid}/retry", async (Guid id, RetryTripRequest? req, ISender sender) =>
        {
            var command = new ReissueTripCommand(
                OriginalTripId: id,
                RetrySource: req?.Source ?? "Manual",
                RetriedBy: req?.RetriedBy,
                RetryReason: req?.Reason,
                CorrelationId: req?.CorrelationId);
            var result = await sender.Send(command);
            return result.IsSuccess
                ? Results.Created($"/api/v1/dispatch/trips/{result.Value}", new { newTripId = result.Value })
                : Results.BadRequest(result.Error);
        }).RequirePermission("dtms:trip:retry");

        // POST /api/v1/dispatch/trips/{id}/exceptions — Raise an exception
        group.MapPost("/trips/{id:guid}/exceptions", async (Guid id, RaiseExceptionRequest req, ISender sender) =>
        {
            var result = await sender.Send(new RaiseExceptionCommand(id, req.Code, req.Severity, req.Detail));
            return result.IsSuccess
                ? Results.Created($"/api/v1/dispatch/trips/{id}/exceptions/{result.Value}", result.Value)
                : Results.BadRequest(result.Error);
        }).RequirePermission("dtms:trip:exception");

        // POST /api/v1/dispatch/trips/{tripId}/exceptions/{exceptionId}/resolve
        group.MapPost("/trips/{tripId:guid}/exceptions/{exceptionId:guid}/resolve",
            async (Guid tripId, Guid exceptionId, ResolveExceptionRequest req, ISender sender) =>
            {
                var result = await sender.Send(new ResolveExceptionCommand(tripId, exceptionId, req.Resolution, req.ResolvedBy));
                return result.IsSuccess ? Results.Ok() : Results.BadRequest(result.Error);
            }).RequirePermission("dtms:trip:exception");

        // POST /api/v1/dispatch/trips/{id}/pod — Capture Proof of Delivery
        group.MapPost("/trips/{id:guid}/pod", async (Guid id, CapturePodRequest req, ISender sender) =>
        {
            var result = await sender.Send(new CapturePodCommand(id, req.StopId, req.PhotoUrl, req.SignatureData, req.ScannedIds, req.Notes));
            return result.IsSuccess
                ? Results.Created($"/api/v1/dispatch/trips/{id}/pod/{result.Value}", result.Value)
                : Results.BadRequest(result.Error);
        }).RequirePermission("dtms:trip:pod");
    }
}

public record RaiseExceptionRequest(string Code, string Severity, string Detail);
public record ResolveExceptionRequest(string Resolution, string ResolvedBy);
public record CapturePodRequest(Guid StopId, string? PhotoUrl, string? SignatureData, List<string>? ScannedIds, string? Notes);
public record RetryTripRequest(string? Source, string? RetriedBy, string? Reason, Guid? CorrelationId);
