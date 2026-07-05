using DTMS.DeliveryOrder.Application.Commands.AbandonStuckDeliveryOrder;
using DTMS.DeliveryOrder.Application.Commands.AmendDeliveryOrder;
using DTMS.DeliveryOrder.Application.Commands.BulkCancelDeliveryOrders;
using DTMS.DeliveryOrder.Application.Commands.BulkSubmitDeliveryOrders;
using DTMS.DeliveryOrder.Application.Commands.CancelDeliveryOrder;
using DTMS.DeliveryOrder.Application.Commands.ConfirmItemPod;
using DTMS.DeliveryOrder.Application.Commands.CreateDraftDeliveryOrder;
using DTMS.DeliveryOrder.Application.Commands.CreateUpstreamDeliveryOrder;
using DTMS.DeliveryOrder.Application.Commands.HoldDeliveryOrder;
using DTMS.DeliveryOrder.Application.Commands.RedispatchDeliveryOrder;
using DTMS.DeliveryOrder.Application.Commands.RejectDeliveryOrder;
using DTMS.DeliveryOrder.Application.Commands.ReleaseDeliveryOrder;
using DTMS.DeliveryOrder.Application.Commands.ReopenDeliveryOrder;
using DTMS.DeliveryOrder.Application.Commands.ResendOmsArrivedNotification;
using DTMS.DeliveryOrder.Application.Commands.ResendOmsNotification;
using DTMS.DeliveryOrder.Application.Commands.SubmitDeliveryOrder;
using DTMS.DeliveryOrder.Application.Commands.UpdateDraftDeliveryOrder;
using DTMS.DeliveryOrder.Application.Queries.GetDeliveryOrder;
using DTMS.DeliveryOrder.Application.Queries.GetFullOrderAudit;
using DTMS.DeliveryOrder.Application.Queries.GetItem;
using DTMS.DeliveryOrder.Application.Queries.GetOrderItems;
using DTMS.DeliveryOrder.Application.Queries.GetOrderStatusHistory;
using DTMS.DeliveryOrder.Application.Queries.GetOrderTimeline;
using DTMS.DeliveryOrder.Domain.Enums;
using DTMS.DeliveryOrder.Presentation.Idempotency;
using DTMS.Iam.Application.Authorization;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace DTMS.DeliveryOrder.Presentation;

public record CancelOrderRequest(string Reason);
public record RejectOrderRequest(string Reason, string? RejectedBy = null);
public record HoldOrderRequest(string Reason, string? HeldBy = null);
public record ReleaseOrderRequest(string? ReleasedBy = null);
public record ReopenOrderRequest(string ReopenedBy, string Reason);
public record AbandonOrderRequest(string AbandonedBy, string Reason);
public record RedispatchOrderRequest(string RedispatchedBy, string Reason, double WeightFallbackKg = 0);
public record ResendOmsNotificationRequest(string? RequestedBy = null);
// ScanType: "Pickup" | "Drop" (case-insensitive enum binding); defaults
// to Drop for backward-compatibility with clients on the pre-pickup-POD
// schema. Drop semantics match the legacy /pod-scan endpoint exactly.
public record ConfirmItemPodRequest(string ScannedBy, string Method, string? Reference = null, PodScanType ScanType = PodScanType.Drop);
public record ConfirmItemPodBatchRequest(string ScannedBy, string Method, IReadOnlyList<ConfirmItemPodBatchEntry> Scans, PodScanType ScanType = PodScanType.Drop);
public record ConfirmItemPodBatchEntry(Guid ItemId, string? Reference);

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
        }).RequireIdempotencyKey().RequirePermission(Permissions.DeliveryOrder.OrderWrite);

        // POST /api/v1/delivery-orders/{id}/submit — submit draft (Draft → Validated)
        group.MapPost("/{id:guid}/submit", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new SubmitDeliveryOrderCommand(id));
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        }).RequireIdempotencyKey().RequirePermission(Permissions.DeliveryOrder.OrderSubmit);

        // POST /api/v1/delivery-orders/{id}/confirm was removed in Phase P5.
        // Submit now auto-confirms atomically (Draft → Confirmed in one
        // handler call, mirroring the system path), so a separate manual
        // confirmation step no longer exists. The `dtms:order:confirm`
        // permission is dropped alongside it in the same phase migration.

        // POST /api/v1/delivery-orders/{id}/reject — reject (Submitted|Validated|Confirmed → Rejected)
        group.MapPost("/{id:guid}/reject", async (Guid id, [FromBody] RejectOrderRequest body, ISender sender) =>
        {
            var result = await sender.Send(new RejectDeliveryOrderCommand(id, body.Reason, body.RejectedBy));
            return result.IsSuccess ? Results.NoContent() : Results.BadRequest(result.Error);
        }).RequireIdempotencyKey().RequirePermission(Permissions.DeliveryOrder.OrderReject);

        // POST /api/v1/delivery-orders/{id}/hold — hold an order (any live state → Held)
        group.MapPost("/{id:guid}/hold", async (Guid id, [FromBody] HoldOrderRequest body, ISender sender) =>
        {
            var result = await sender.Send(new HoldDeliveryOrderCommand(id, body.Reason, body.HeldBy));
            return result.IsSuccess ? Results.NoContent() : Results.BadRequest(result.Error);
        }).RequireIdempotencyKey().RequirePermission(Permissions.DeliveryOrder.OrderHold);

        // POST /api/v1/delivery-orders/{id}/release — release a held order back to Confirmed
        // (re-fires DeliveryOrderConfirmedIntegrationEvent so Planning re-plans).
        group.MapPost("/{id:guid}/release", async (Guid id, [FromBody] ReleaseOrderRequest? body, ISender sender) =>
        {
            var result = await sender.Send(new ReleaseDeliveryOrderCommand(id, body?.ReleasedBy));
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        }).RequireIdempotencyKey().RequirePermission(Permissions.DeliveryOrder.OrderHold);

        // POST /api/v1/delivery-orders/{id}/reopen — admin override: bring a
        // Failed order back to Confirmed so the operator can call
        // /dispatch/trips/{tripId}/retry on its failed trip(s). Does NOT
        // auto-retry — the two actions are audited separately on purpose.
        group.MapPost("/{id:guid}/reopen", async (Guid id, [FromBody] ReopenOrderRequest body, ISender sender) =>
        {
            var result = await sender.Send(new ReopenDeliveryOrderCommand(id, body.ReopenedBy, body.Reason));
            return result.IsSuccess ? Results.NoContent() : Results.BadRequest(result.Error);
        }).RequireIdempotencyKey().RequirePermission(Permissions.DeliveryOrder.OrderReopen);

        // POST /api/v1/delivery-orders/{id}/abandon-after-trip-cancel —
        // Phase b11 escape hatch (Option B). Operator-driven close-out
        // for orders stranded at an in-flight status (typically Dispatched)
        // with zero active trips. Pre-b11 legacy data + edge cases where
        // the TripCancelledConsumer cascade didn't fire. Validates BOTH
        // preconditions in the handler — rejects with 400 otherwise.
        group.MapPost("/{id:guid}/abandon-after-trip-cancel",
            async (Guid id, [FromBody] AbandonOrderRequest body, ISender sender) =>
        {
            var result = await sender.Send(new AbandonStuckDeliveryOrderCommand(id, body.AbandonedBy, body.Reason));
            return result.IsSuccess ? Results.NoContent() : Results.BadRequest(result.Error);
        }).RequireIdempotencyKey().RequirePermission(Permissions.DeliveryOrder.OrderAbandon);

        // POST /api/v1/delivery-orders/{id}/items/{itemId}/pod-scan —
        // Operator submits a POD scan for one item. Body.scanType selects
        // the checkpoint: "Pickup" records audit only; "Drop" (default)
        // transitions DroppedOff/Picked → Delivered on RequiresDropPod
        // orders. Idempotent against duplicate scans at the same checkpoint.
        group.MapPost("/{id:guid}/items/{itemId:guid}/pod-scan",
            async (Guid id, Guid itemId, [FromBody] ConfirmItemPodRequest body, ISender sender) =>
        {
            var result = await sender.Send(new ConfirmItemPodCommand(
                id, itemId, body.ScanType, body.ScannedBy, body.Method, body.Reference));
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.BadRequest(result.Error);
        }).RequireIdempotencyKey().RequirePermission(Permissions.DeliveryOrder.OrderPod);

        // POST /api/v1/delivery-orders/{id}/pod-batch — Bulk POD scan
        // for trip-level confirmation (one operator action covers
        // every item). Returns per-item results so the UI can render
        // which scans landed vs. which were already-Delivered no-ops.
        group.MapPost("/{id:guid}/pod-batch",
            async (Guid id, [FromBody] ConfirmItemPodBatchRequest body, ISender sender) =>
        {
            var results = new List<object>();
            int confirmed = 0, skipped = 0;
            foreach (var scan in body.Scans)
            {
                var r = await sender.Send(new ConfirmItemPodCommand(
                    id, scan.ItemId, body.ScanType, body.ScannedBy, body.Method, scan.Reference));
                if (r.IsSuccess)
                {
                    if (r.Value!.Confirmed) confirmed++; else skipped++;
                    results.Add(new { itemId = scan.ItemId, ok = true, confirmed = r.Value.Confirmed, status = r.Value.ItemStatus });
                }
                else
                {
                    results.Add(new { itemId = scan.ItemId, ok = false, error = r.Error });
                }
            }
            return Results.Ok(new { confirmed, skipped, results });
        }).RequireIdempotencyKey().RequirePermission(Permissions.DeliveryOrder.OrderPod);

        // POST /api/v1/delivery-orders/{id}/redispatch — recovery for orders
        // whose dispatch produced no Trip at all (every group failed at
        // vendor / no OrderTemplate registered). Re-fires the Confirmed
        // integration event so Planning's consumer re-runs. Rejects if
        // any Trip is still active — operator should use /trips/{id}/retry
        // in that case. Requires Confirmed state (operator usually
        // /reopen first, fixes the underlying issue, then /redispatch).
        group.MapPost("/{id:guid}/redispatch", async (Guid id, [FromBody] RedispatchOrderRequest body, ISender sender) =>
        {
            var result = await sender.Send(new RedispatchDeliveryOrderCommand(
                id, body.RedispatchedBy, body.Reason, body.WeightFallbackKg));
            return result.IsSuccess ? Results.NoContent() : Results.BadRequest(result.Error);
        }).RequireIdempotencyKey().RequirePermission(Permissions.DeliveryOrder.OrderRedispatch);

        // POST /api/v1/delivery-orders/{id}/trips/{tripId}/notify-oms —
        // Operator-driven manual resend of the upstream-OMS shipment
        // notification for a specific trip. Use when the automatic
        // consumer dead-lettered (UpstreamOmsNotifyFailed audit) and
        // the upstream OMS issue has since been fixed. Calls the OMS
        // client synchronously so the operator sees immediate feedback;
        // upstream is expected to dedupe by shipmentId.
        group.MapPost("/{id:guid}/trips/{tripId:guid}/notify-oms",
            async (Guid id, Guid tripId, [FromBody] ResendOmsNotificationRequest? body, ISender sender) =>
        {
            var result = await sender.Send(new ResendOmsNotificationCommand(id, tripId, body?.RequestedBy));
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        }).RequireIdempotencyKey().RequirePermission(Permissions.DeliveryOrder.OrderNotifyOms);

        // POST /api/v1/delivery-orders/{id}/trips/{tripId}/notify-oms-arrived —
        // Mirror of /notify-oms but for the /arrived (drop completed)
        // endpoint. Surfaces a separate Resend button on the UI for
        // when the auto consumer dead-lettered the drop notification.
        group.MapPost("/{id:guid}/trips/{tripId:guid}/notify-oms-arrived",
            async (Guid id, Guid tripId, [FromBody] ResendOmsNotificationRequest? body, ISender sender) =>
        {
            var result = await sender.Send(new ResendOmsArrivedNotificationCommand(id, tripId, body?.RequestedBy));
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        }).RequireIdempotencyKey().RequirePermission(Permissions.DeliveryOrder.OrderNotifyOms);

        // POST /api/v1/delivery-orders/upstream was removed in Phase P4 of
        // the SourceSystem migration. External systems now hit the federated
        // /api/v1/source/delivery-orders endpoint, which authenticates via
        // SystemClientAuthMiddleware and stamps SourceSystemKey from the JWT
        // sub claim (Phase S.8e P3 dropped the URL {key} segment). The
        // dtms:order:upstream permission is retired alongside it.

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
        }).RequireIdempotencyKey().RequirePermission(Permissions.DeliveryOrder.OrderBulk);

        // POST /api/v1/delivery-orders/bulk-cancel — Backend Phase 2
        // Body: { orderIds: [guid...], reason: string }
        // Returns 200 if everything cancelled, 207 Multi-Status if some
        // rows failed (e.g. already-Cancelled or wrong state), 400 if
        // every id failed or the request shape was invalid.
        group.MapPost("/bulk-cancel", async (BulkCancelDeliveryOrdersCommand command, ISender sender) =>
        {
            var result = await sender.Send(command);
            if (result.IsFailure) return Results.BadRequest(result.Error);

            var bulk = result.Value;
            if (bulk.Succeeded.Count == 0)
                return Results.BadRequest(bulk.Failures);

            return bulk.Failures.Count > 0
                ? Results.Json(bulk, statusCode: 207)
                : Results.Ok(bulk);
        }).RequireIdempotencyKey().RequirePermission(Permissions.DeliveryOrder.OrderCancel);

        // GET /api/v1/delivery-orders/{id}
        group.MapGet("/{id:guid}", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new GetDeliveryOrderQuery(id));
            return result.IsSuccess ? Results.Ok(result.Value) : Results.NotFound(result.Error);
        }).RequirePermission(Permissions.DeliveryOrder.OrderRead);

        // GET /api/v1/delivery-orders?status=&statusBucket=&priority=&transportMode=&search=&sortBy=&sortDir=&page=&pageSize=&createdFromUtc=&createdToUtc=
        group.MapGet("/", async (
            string? status,
            string? statusBucket,
            string? priority,
            string? transportMode,
            string? search,
            bool? hasFailedTrip,
            bool? hasActiveJob,
            string? sortBy,
            string? sortDir,
            DateTime? createdFromUtc,
            DateTime? createdToUtc,
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
            // Normalise window bounds to UTC kind so EF translates them
            // to `timestamp with time zone` correctly even when the caller
            // sent a Local-kind ISO string.
            var fromUtc = createdFromUtc.HasValue
                ? DateTime.SpecifyKind(createdFromUtc.Value.ToUniversalTime(), DateTimeKind.Utc)
                : (DateTime?)null;
            var toUtc = createdToUtc.HasValue
                ? DateTime.SpecifyKind(createdToUtc.Value.ToUniversalTime(), DateTimeKind.Utc)
                : (DateTime?)null;

            var query = new GetDeliveryOrdersQuery(
                orderStatus,
                bucket,
                orderPriority,
                mode,
                string.IsNullOrWhiteSpace(search) ? null : search.Trim(),
                hasFailedTrip,
                hasActiveJob,
                string.IsNullOrWhiteSpace(sortBy) ? null : sortBy.Trim(),
                descending,
                clampedPage,
                clampedSize,
                fromUtc,
                toUtc);

            var result = await sender.Send(query);
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        }).RequirePermission(Permissions.DeliveryOrder.OrderRead);

        // GET /api/v1/delivery-orders/stats — aggregate counts for the KPI strip
        // and filter chips. Unfiltered by design: the strip is a system-wide
        // overview, not a "what's in your current view" readout.
        group.MapGet("/stats", async (ISender sender) =>
        {
            var result = await sender.Send(new GetDeliveryOrderStatsQuery());
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        }).RequirePermission(Permissions.DeliveryOrder.OrderRead);

        // DELETE /api/v1/delivery-orders/{id}
        group.MapDelete("/{id:guid}", async (Guid id, [FromBody] CancelOrderRequest body, ISender sender) =>
        {
            var result = await sender.Send(new CancelDeliveryOrderCommand(id, body.Reason));
            return result.IsSuccess ? Results.NoContent() : Results.BadRequest(result.Error);
        }).RequireIdempotencyKey().RequirePermission(Permissions.DeliveryOrder.OrderCancel);

        // PUT /api/v1/delivery-orders/{id} — replace draft (only allowed when status=Draft)
        group.MapPut("/{id:guid}", async (Guid id, UpdateDraftDeliveryOrderCommand command, ISender sender) =>
        {
            var result = await sender.Send(command with { OrderId = id });
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        }).RequireIdempotencyKey().RequirePermission(Permissions.DeliveryOrder.OrderWrite);

        // PATCH /api/v1/delivery-orders/{id} — amendment
        group.MapPatch("/{id:guid}", async (Guid id, AmendDeliveryOrderCommand command, ISender sender) =>
        {
            var result = await sender.Send(command with { OrderId = id });
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        }).RequireIdempotencyKey().RequirePermission(Permissions.DeliveryOrder.OrderWrite);

        // GET /api/v1/delivery-orders/{id}/timeline
        group.MapGet("/{id:guid}/timeline", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new GetOrderTimelineQuery(id));
            return result.IsSuccess ? Results.Ok(result.Value) : Results.NotFound(result.Error);
        }).RequirePermission(Permissions.DeliveryOrder.OrderRead);

        // GET /api/v1/delivery-orders/{id}/status-history — Phase P1 (b12)
        // Structured status-transition timeline materialized by
        // OrderStatusHistoryProjector. Backs the <StatusTimelineSection /> in
        // the operator drawer. Returns 200 with empty array when the order
        // exists but no projection rows have arrived yet (legacy / unconfirmed).
        group.MapGet("/{id:guid}/status-history", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new GetOrderStatusHistoryQuery(id));
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        }).RequirePermission(Permissions.DeliveryOrder.OrderRead);

        // GET /api/v1/delivery-orders/{id}/audit-full — consolidated
        // audit log: OrderAuditEvents + amendments + per-trip execution
        // events + retry triggers, sorted newest-first. Backs the
        // operator / support "what happened?" drawer view (Phase 4.2).
        group.MapGet("/{id:guid}/audit-full", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new GetFullOrderAuditQuery(id));
            return result.IsSuccess ? Results.Ok(result.Value) : Results.NotFound(result.Error);
        }).RequirePermission(Permissions.DeliveryOrder.OrderRead);

        // GET /api/v1/delivery-orders/{id}/items?status=
        group.MapGet("/{id:guid}/items", async (Guid id, string? status, ISender sender) =>
        {
            ItemStatus? itemStatus = status != null && Enum.TryParse<ItemStatus>(status, true, out var s) ? s : null;
            var result = await sender.Send(new GetOrderItemsQuery(id, itemStatus));
            return result.IsSuccess ? Results.Ok(result.Value) : Results.NotFound(result.Error);
        }).RequirePermission(Permissions.DeliveryOrder.OrderRead);

        // GET /api/v1/delivery-orders/{id}/items/{itemId}
        group.MapGet("/{id:guid}/items/{itemId:guid}", async (Guid id, Guid itemId, ISender sender) =>
        {
            var result = await sender.Send(new GetItemQuery(itemId));
            if (result.IsFailure) return Results.NotFound(result.Error);
            if (result.Value.DeliveryOrderId != id)
                return Results.NotFound($"Item {itemId} not found in order {id}.");

            return Results.Ok(result.Value);
        }).RequirePermission(Permissions.DeliveryOrder.OrderRead);
    }
}
