using AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.AmendDeliveryOrder;
using AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.BulkSubmitDeliveryOrders;
using AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.CancelDeliveryOrder;
using AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.ConfirmDeliveryOrder;
using AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.CreateDraftDeliveryOrder;
using AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.CreateUpstreamDeliveryOrder;
using AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.HoldDeliveryOrder;
using AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.RejectDeliveryOrder;
using AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.ReleaseDeliveryOrder;
using AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.ReopenDeliveryOrder;
using AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.SubmitDeliveryOrder;
using AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.UpdateDraftDeliveryOrder;
using AMR.DeliveryPlanning.DeliveryOrder.Application.Queries.GetDeliveryOrder;
using AMR.DeliveryPlanning.DeliveryOrder.Application.Queries.GetItem;
using AMR.DeliveryPlanning.DeliveryOrder.Application.Queries.GetOrderItems;
using AMR.DeliveryPlanning.DeliveryOrder.Application.Queries.GetOrderTimeline;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.DeliveryOrder.Presentation.Idempotency;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace AMR.DeliveryPlanning.DeliveryOrder.Presentation;

public record CancelOrderRequest(string Reason);
public record ConfirmOrderRequest(string? ConfirmedBy = null);
public record RejectOrderRequest(string Reason, string? RejectedBy = null);
public record HoldOrderRequest(string Reason, string? HeldBy = null);
public record ReleaseOrderRequest(string? ReleasedBy = null);
public record ReopenOrderRequest(string ReopenedBy, string Reason);

public static class DeliveryOrderEndpoints
{
    public static void MapDeliveryOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/delivery-orders").WithTags("DeliveryOrders").RequireAuthorization();

        // POST /api/v1/delivery-orders — create draft. Requires Idempotency-Key.
        // To submit, call POST /{id}/submit after creating.
        group.MapPost("/", async (CreateDraftDeliveryOrderCommand command, ISender sender) =>
        {
            var result = await sender.Send(command);
            return result.IsSuccess
                ? Results.Created($"/api/v1/delivery-orders/{result.Value.Id}", result.Value)
                : Results.BadRequest(result.Error);
        }).RequireIdempotencyKey();

        // POST /api/v1/delivery-orders/{id}/submit — submit draft (Draft → Validated)
        group.MapPost("/{id:guid}/submit", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new SubmitDeliveryOrderCommand(id));
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        }).RequireIdempotencyKey();

        // POST /api/v1/delivery-orders/{id}/confirm — manual confirm (Validated → Confirmed)
        group.MapPost("/{id:guid}/confirm", async (Guid id, [FromBody] ConfirmOrderRequest? body, ISender sender) =>
        {
            var result = await sender.Send(new ConfirmDeliveryOrderCommand(id, body?.ConfirmedBy));
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        }).RequireIdempotencyKey();

        // POST /api/v1/delivery-orders/{id}/reject — reject (Submitted|Validated|Confirmed → Rejected)
        group.MapPost("/{id:guid}/reject", async (Guid id, [FromBody] RejectOrderRequest body, ISender sender) =>
        {
            var result = await sender.Send(new RejectDeliveryOrderCommand(id, body.Reason, body.RejectedBy));
            return result.IsSuccess ? Results.NoContent() : Results.BadRequest(result.Error);
        }).RequireIdempotencyKey();

        // POST /api/v1/delivery-orders/{id}/hold — hold an order (any live state → Held)
        group.MapPost("/{id:guid}/hold", async (Guid id, [FromBody] HoldOrderRequest body, ISender sender) =>
        {
            var result = await sender.Send(new HoldDeliveryOrderCommand(id, body.Reason, body.HeldBy));
            return result.IsSuccess ? Results.NoContent() : Results.BadRequest(result.Error);
        }).RequireIdempotencyKey();

        // POST /api/v1/delivery-orders/{id}/release — release a held order back to Confirmed
        // (re-fires DeliveryOrderConfirmedIntegrationEvent so Planning re-plans).
        group.MapPost("/{id:guid}/release", async (Guid id, [FromBody] ReleaseOrderRequest? body, ISender sender) =>
        {
            var result = await sender.Send(new ReleaseDeliveryOrderCommand(id, body?.ReleasedBy));
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        }).RequireIdempotencyKey();

        // POST /api/v1/delivery-orders/{id}/reopen — admin override: bring a
        // Failed order back to Confirmed so the operator can call
        // /dispatch/trips/{tripId}/retry on its failed trip(s). Does NOT
        // auto-retry — the two actions are audited separately on purpose.
        group.MapPost("/{id:guid}/reopen", async (Guid id, [FromBody] ReopenOrderRequest body, ISender sender) =>
        {
            var result = await sender.Send(new ReopenDeliveryOrderCommand(id, body.ReopenedBy, body.Reason));
            return result.IsSuccess ? Results.NoContent() : Results.BadRequest(result.Error);
        }).RequireIdempotencyKey();

        // POST /api/v1/delivery-orders/upstream — auto pipeline for upstream sources (SAP/ERP/OMS)
        // Submitted → Validated → Confirmed in one transaction.
        // Idempotent on (SourceSystem, OrderRef) at DB-level; Idempotency-Key is the
        // transport-level guard for client retries.
        group.MapPost("/upstream", async (CreateUpstreamDeliveryOrderCommand command, ISender sender) =>
        {
            var result = await sender.Send(command);
            return result.IsSuccess
                ? Results.Created($"/api/v1/delivery-orders/{result.Value.Id}", result.Value)
                : Results.BadRequest(result.Error);
        }).RequireIdempotencyKey();

        // POST /api/v1/delivery-orders/bulk
        group.MapPost("/bulk", async (BulkSubmitDeliveryOrdersCommand command, ISender sender) =>
        {
            var result = await sender.Send(command);
            if (result.IsFailure) return Results.BadRequest(result.Error);

            var bulk = result.Value;
            if (bulk.SucceededIds.Count == 0)
                return Results.BadRequest(bulk.Failures);

            return bulk.Failures.Count > 0
                ? Results.Json(bulk, statusCode: 207)
                : Results.Ok(bulk);
        }).RequireIdempotencyKey();

        // GET /api/v1/delivery-orders/{id}
        group.MapGet("/{id:guid}", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new GetDeliveryOrderQuery(id));
            return result.IsSuccess ? Results.Ok(result.Value) : Results.NotFound(result.Error);
        });

        // GET /api/v1/delivery-orders?status=&statusBucket=&priority=&transportMode=&search=&sortBy=&sortDir=&page=&pageSize=
        group.MapGet("/", async (
            string? status,
            string? statusBucket,
            string? priority,
            string? transportMode,
            string? search,
            string? sortBy,
            string? sortDir,
            ISender sender,
            int page = 1,
            int pageSize = 20) =>
        {
            OrderStatus? orderStatus = status != null && Enum.TryParse<OrderStatus>(status, true, out var s) ? s : null;
            StatusBucket? bucket = statusBucket != null && Enum.TryParse<StatusBucket>(statusBucket, true, out var b) ? b : null;
            Priority? orderPriority = priority != null && Enum.TryParse<Priority>(priority, true, out var p) ? p : null;
            TransportMode? mode = transportMode != null && Enum.TryParse<TransportMode>(transportMode, true, out var m) ? m : null;
            // Sort defaults to newest first — the dispatcher's mental model.
            var descending = !string.Equals(sortDir, "asc", StringComparison.OrdinalIgnoreCase);
            var clampedPage = page <= 0 ? 1 : page;
            var clampedSize = pageSize is <= 0 or > 200 ? 20 : pageSize;

            var query = new GetDeliveryOrdersQuery(
                orderStatus,
                bucket,
                orderPriority,
                mode,
                string.IsNullOrWhiteSpace(search) ? null : search.Trim(),
                string.IsNullOrWhiteSpace(sortBy) ? null : sortBy.Trim(),
                descending,
                clampedPage,
                clampedSize);

            var result = await sender.Send(query);
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        });

        // GET /api/v1/delivery-orders/stats — aggregate counts for the KPI strip
        // and filter chips. Unfiltered by design: the strip is a system-wide
        // overview, not a "what's in your current view" readout.
        group.MapGet("/stats", async (ISender sender) =>
        {
            var result = await sender.Send(new GetDeliveryOrderStatsQuery());
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        });

        // DELETE /api/v1/delivery-orders/{id}
        group.MapDelete("/{id:guid}", async (Guid id, [FromBody] CancelOrderRequest body, ISender sender) =>
        {
            var result = await sender.Send(new CancelDeliveryOrderCommand(id, body.Reason));
            return result.IsSuccess ? Results.NoContent() : Results.BadRequest(result.Error);
        }).RequireIdempotencyKey();

        // PUT /api/v1/delivery-orders/{id} — replace draft (only allowed when status=Draft)
        group.MapPut("/{id:guid}", async (Guid id, UpdateDraftDeliveryOrderCommand command, ISender sender) =>
        {
            var result = await sender.Send(command with { OrderId = id });
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        }).RequireIdempotencyKey();

        // PATCH /api/v1/delivery-orders/{id} — amendment
        group.MapPatch("/{id:guid}", async (Guid id, AmendDeliveryOrderCommand command, ISender sender) =>
        {
            var result = await sender.Send(command with { OrderId = id });
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        }).RequireIdempotencyKey();

        // GET /api/v1/delivery-orders/{id}/timeline
        group.MapGet("/{id:guid}/timeline", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new GetOrderTimelineQuery(id));
            return result.IsSuccess ? Results.Ok(result.Value) : Results.NotFound(result.Error);
        });

        // GET /api/v1/delivery-orders/{id}/items?status=
        group.MapGet("/{id:guid}/items", async (Guid id, string? status, ISender sender) =>
        {
            ItemStatus? itemStatus = status != null && Enum.TryParse<ItemStatus>(status, true, out var s) ? s : null;
            var result = await sender.Send(new GetOrderItemsQuery(id, itemStatus));
            return result.IsSuccess ? Results.Ok(result.Value) : Results.NotFound(result.Error);
        });

        // GET /api/v1/delivery-orders/{id}/items/{itemId}
        group.MapGet("/{id:guid}/items/{itemId:guid}", async (Guid id, Guid itemId, ISender sender) =>
        {
            var result = await sender.Send(new GetItemQuery(itemId));
            if (result.IsFailure) return Results.NotFound(result.Error);
            if (result.Value.DeliveryOrderId != id)
                return Results.NotFound($"Item {itemId} not found in order {id}.");

            return Results.Ok(result.Value);
        });
    }
}
