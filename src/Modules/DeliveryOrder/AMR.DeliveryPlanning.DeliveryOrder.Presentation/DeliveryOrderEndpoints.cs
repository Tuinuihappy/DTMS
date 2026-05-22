using AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.AmendDeliveryOrder;
using AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.BulkSubmitDeliveryOrders;
using AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.CancelDeliveryOrder;
using AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.ConfirmDeliveryOrder;
using AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.CreateDraftDeliveryOrder;
using AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.CreateUpstreamDeliveryOrder;
using AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.RejectDeliveryOrder;
using AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.SubmitDeliveryOrder;
using AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.UpdateDraftDeliveryOrder;
using AMR.DeliveryPlanning.DeliveryOrder.Application.Queries.GetDeliveryOrder;
using AMR.DeliveryPlanning.DeliveryOrder.Application.Queries.GetOrderItems;
using AMR.DeliveryPlanning.DeliveryOrder.Application.Queries.GetOrderTimeline;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Distributed;

namespace AMR.DeliveryPlanning.DeliveryOrder.Presentation;

public record CancelOrderRequest(string Reason);
public record ConfirmOrderRequest(string? ConfirmedBy = null);
public record RejectOrderRequest(string Reason, string? RejectedBy = null);

public static class DeliveryOrderEndpoints
{
    public static void MapDeliveryOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/delivery-orders").WithTags("DeliveryOrders").RequireAuthorization();

        // POST /api/delivery-orders — create draft (supports Idempotency-Key header).
        // To submit, call POST /{id}/submit after creating.
        group.MapPost("/", async (CreateDraftDeliveryOrderCommand command, HttpContext ctx, ISender sender, IDistributedCache cache) =>
        {
            var idempotencyKey = ctx.Request.Headers["Idempotency-Key"].FirstOrDefault();

            if (idempotencyKey != null)
            {
                var existing = await cache.GetStringAsync($"idempotency:{idempotencyKey}");
                if (existing != null && Guid.TryParse(existing, out var existingId))
                {
                    var cached = await sender.Send(new GetDeliveryOrderQuery(existingId));
                    return cached.IsSuccess
                        ? Results.Ok(cached.Value)
                        : Results.Created($"/api/delivery-orders/{existingId}", existingId);
                }
            }

            var result = await sender.Send(command);
            if (!result.IsSuccess) return Results.BadRequest(result.Error);

            if (idempotencyKey != null)
                await cache.SetStringAsync($"idempotency:{idempotencyKey}", result.Value.Id.ToString(),
                    new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24) });

            return Results.Created($"/api/delivery-orders/{result.Value.Id}", result.Value);
        });

        // POST /api/delivery-orders/{id}/submit — submit draft (Draft → Validated)
        group.MapPost("/{id:guid}/submit", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new SubmitDeliveryOrderCommand(id));
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        });

        // POST /api/delivery-orders/{id}/confirm — manual confirm (Validated → Confirmed → ReadyToPlan)
        group.MapPost("/{id:guid}/confirm", async (Guid id, [FromBody] ConfirmOrderRequest? body, ISender sender) =>
        {
            var result = await sender.Send(new ConfirmDeliveryOrderCommand(id, body?.ConfirmedBy));
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        });

        // POST /api/delivery-orders/{id}/reject — reject (Submitted|Validated|Confirmed → Rejected)
        group.MapPost("/{id:guid}/reject", async (Guid id, [FromBody] RejectOrderRequest body, ISender sender) =>
        {
            var result = await sender.Send(new RejectDeliveryOrderCommand(id, body.Reason, body.RejectedBy));
            return result.IsSuccess ? Results.NoContent() : Results.BadRequest(result.Error);
        });

        // POST /api/delivery-orders/upstream — auto pipeline for upstream sources (SAP/ERP/OMS)
        // Submitted → Validated → Confirmed → ReadyToPlan in one transaction.
        // Idempotent on (SourceSystem, OrderRef): retries return the existing order id.
        group.MapPost("/upstream", async (CreateUpstreamDeliveryOrderCommand command, ISender sender) =>
        {
            var result = await sender.Send(command);
            return result.IsSuccess
                ? Results.Created($"/api/delivery-orders/{result.Value.Id}", result.Value)
                : Results.BadRequest(result.Error);
        });

        // POST /api/delivery-orders/bulk
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
        });

        // GET /api/delivery-orders/{id}
        group.MapGet("/{id:guid}", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new GetDeliveryOrderQuery(id));
            return result.IsSuccess ? Results.Ok(result.Value) : Results.NotFound(result.Error);
        });

        // GET /api/delivery-orders?status=&page=&pageSize=
        group.MapGet("/", async (string? status, ISender sender, int page = 1, int pageSize = 20) =>
        {
            OrderStatus? orderStatus = status != null && Enum.TryParse<OrderStatus>(status, true, out var s) ? s : null;
            var result = await sender.Send(new GetDeliveryOrdersQuery(orderStatus, page <= 0 ? 1 : page, pageSize <= 0 ? 20 : pageSize));
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        });

        // DELETE /api/delivery-orders/{id}
        group.MapDelete("/{id:guid}", async (Guid id, [FromBody] CancelOrderRequest body, ISender sender) =>
        {
            var result = await sender.Send(new CancelDeliveryOrderCommand(id, body.Reason));
            return result.IsSuccess ? Results.NoContent() : Results.BadRequest(result.Error);
        });

        // PUT /api/delivery-orders/{id} — replace draft (only allowed when status=Draft)
        group.MapPut("/{id:guid}", async (Guid id, UpdateDraftDeliveryOrderCommand command, ISender sender) =>
        {
            var result = await sender.Send(command with { OrderId = id });
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        });

        // PATCH /api/delivery-orders/{id} — amendment
        group.MapPatch("/{id:guid}", async (Guid id, AmendDeliveryOrderCommand command, ISender sender) =>
        {
            var result = await sender.Send(command with { OrderId = id });
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        });

        // GET /api/delivery-orders/{id}/timeline
        group.MapGet("/{id:guid}/timeline", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new GetOrderTimelineQuery(id));
            return result.IsSuccess ? Results.Ok(result.Value) : Results.NotFound(result.Error);
        });

        // GET /api/delivery-orders/{id}/items?status=
        group.MapGet("/{id:guid}/items", async (Guid id, string? status, ISender sender) =>
        {
            ItemStatus? itemStatus = status != null && Enum.TryParse<ItemStatus>(status, true, out var s) ? s : null;
            var result = await sender.Send(new GetOrderItemsQuery(id, itemStatus));
            return result.IsSuccess ? Results.Ok(result.Value) : Results.NotFound(result.Error);
        });

        // GET /api/delivery-orders/{id}/items/{itemId}
        group.MapGet("/{id:guid}/items/{itemId:guid}", async (Guid id, Guid itemId, ISender sender) =>
        {
            var result = await sender.Send(new GetOrderItemsQuery(id));
            if (!result.IsSuccess) return Results.NotFound(result.Error);

            var item = result.Value.FirstOrDefault(i => i.Id == itemId);
            return item is not null ? Results.Ok(item) : Results.NotFound($"Item {itemId} not found in order {id}.");
        });
    }
}
