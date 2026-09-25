using DTMS.DeliveryOrder.Application.Commands.CreateDraftDeliveryOrder;
using DTMS.DeliveryOrder.Application.Commands.CreateUpstreamDeliveryOrder;
using DTMS.DeliveryOrder.Application.Commands.SourceCancelDeliveryOrder;
using DTMS.DeliveryOrder.Domain.Enums;
using DTMS.DeliveryOrder.Presentation.Idempotency;
using DTMS.Iam.Application.Authorization;
using DTMS.SharedKernel.Exceptions;
using System.ComponentModel;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace DTMS.DeliveryOrder.Presentation;

/// <summary>
/// Federated source-system endpoint group. Runs under
/// <c>/api/v1/source/*</c> so the
/// <see cref="DTMS.Api.Middlewares"/> system auth + request log middleware
/// applies. Routes the request through
/// <see cref="CreateUpstreamDeliveryOrderCommand"/> — one validated path
/// to "Submitted → Validated → Confirmed".
///
/// <para><b>Phase S.8e (P3) — single canonical URL.</b> Only
/// <c>POST /api/v1/source/delivery-orders</c> exists. The former legacy
/// route <c>/api/v1/source/{key}/delivery-orders</c> was retired here;
/// callers derive their identity from the JWT <c>sub</c> claim, and the
/// URL carries no system slug. See
/// <see cref="DTMS.Api.Middlewares.SystemClientAuthMiddleware"/> for the
/// JWT-first identity resolution.</para>
/// </summary>
public static class SourceSystemDeliveryOrderEndpoints
{
    public static void MapSourceSystemDeliveryOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/source").WithTags("SourceSystem");
        // Idempotency-Key is mandatory here. It is the only retry mechanism on
        // this route: OrderRef is a business reference, so reusing one is a
        // conflict (409) rather than a replay, and a caller with no key would
        // have no safe way to repeat a request whose response it never saw.
        group.MapPost("/delivery-orders", HandleCreateAsync)
            .RequireIdempotencyKey(enforced: true)
            .RequirePermissionFromRouteKey(StandardSystemPermissions.OrderWriteTemplate);

        // Cancel — addressed by the caller's own OrderRef, not the DTMS guid. A
        // source system knows its own reference; making it store and echo our id
        // back would be a second integration for no benefit. The ref is unique
        // only per source system, which is exactly right here: the lookup filters
        // on (caller's key, ref), so the URL cannot name somebody else's order.
        //
        // No Idempotency-Key, matching the six source trip routes. Cancel is
        // naturally idempotent — DeliveryOrder.Cancel early-returns when already
        // Cancelled, so a repeat answers 204 and raises no second event. That is
        // the opposite of the create above, where reusing an OrderRef is a 409
        // and the key is the only safe way to retry.
        //
        // {orderRef} matches ONE path segment; see the OMS doc for what that
        // rules out. No conflict with the create route (segment count differs).
        group.MapPost("/delivery-orders/{orderRef}/cancel", HandleCancelAsync)
            .RequirePermissionFromRouteKey(StandardSystemPermissions.OrderCancelTemplate);
    }

    private static async Task<IResult> HandleCreateAsync(
        [FromBody] CreateSourceOrderRequest body,
        HttpContext ctx,
        ISender sender)
    {
        // Middleware has already validated the JWT and stashed the
        // principal — the null path here would mean the endpoint got
        // mounted outside the /api/v1/source pipeline UseWhen branch,
        // which is a wiring bug, not a runtime input error.
        if (ctx.Items["principal"] is not SystemPrincipal principal)
            return Results.Problem(
                title: "system principal missing",
                detail: "SystemClientAuthMiddleware did not run for this request",
                statusCode: StatusCodes.Status500InternalServerError);

        var command = new CreateUpstreamDeliveryOrderCommand(
            OrderRef: body.OrderRef,
            ServiceWindow: body.ServiceWindow,
            Items: body.Items,
            SourceSystemKey: principal.Key,
            Priority: body.Priority ?? Priority.Normal,
            RequestedBy: body.RequestedBy,
            Notes: body.Notes,
            RequestedTransportMode: body.RequestedTransportMode,
            RequiresDropPod: body.RequiresDropPod,
            RequiresPickupPod: body.RequiresPickupPod,
            SelfManaged: body.SelfManaged);

        var result = await sender.Send(command);
        if (result.IsFailure)
            // Through the exception middleware rather than hand-rolled, so every
            // rejection on /api/v1/source answers one shape (RFC 7807
            // problem+json, with an `error` member kept for compatibility).
            throw new BusinessRuleViolationException(result.Error);

        return Results.Created(
            $"/api/v1/delivery-orders/{result.Value.Order.Id}",
            result.Value);
    }

    private static async Task<IResult> HandleCancelAsync(
        string orderRef,
        [FromBody] SourceCancelOrderRequest? body,
        HttpContext ctx,
        ISender sender)
    {
        // Same wiring-bug guard as HandleCreateAsync — a null principal here
        // means the endpoint got mounted outside the /api/v1/source UseWhen
        // branch, not that the caller sent something wrong.
        if (ctx.Items["principal"] is not SystemPrincipal principal)
            return Results.Problem(
                title: "system principal missing",
                detail: "SystemClientAuthMiddleware did not run for this request",
                statusCode: StatusCodes.Status500InternalServerError);

        if (string.IsNullOrWhiteSpace(body?.ActionBy))
            throw new BusinessRuleViolationException("actionBy is required.");

        // An unknown or foreign ref surfaces as NotFoundException from the
        // handler, which the exception middleware turns into 404.
        var result = await sender.Send(new SourceCancelDeliveryOrderCommand(
            SourceSystemKey: principal.Key,
            OrderRef: orderRef,
            ActionBy: body.ActionBy,
            Reason: body.Reason,
            ActedAt: body.ActedAt));

        if (result.IsFailure)
            throw new BusinessRuleViolationException(result.Error);

        return Results.NoContent();
    }
}

/// <summary>
/// Body for POST /api/v1/source/delivery-orders/{orderRef}/cancel. Deliberately
/// the same shape as the trip route's cancel body, so a caller integrating both
/// writes one payload builder.
/// </summary>
/// <param name="ActionBy">Required. Who cancelled it in the caller's own system.
/// Unlike the trip route — where it lands on ExecutionEvent.Actor — the order
/// side has no actor column the audit timeline reads, so this reaches the
/// application log and nothing else. Required anyway, for parity and because
/// support triage needs it.</param>
/// <param name="Reason">Optional, but please send one: the only field here that
/// reaches the order activity timeline an operator reads. Omitting it falls back
/// to a generic label and logs a warning.</param>
/// <param name="ActedAt">Optional. Same fate as ActionBy — logged, not stored.</param>
public record SourceCancelOrderRequest(
    string ActionBy,
    string? Reason = null,
    DateTime? ActedAt = null);

/// <summary>
/// Inbound payload shape for source-system order creation. Mirrors
/// <see cref="CreateUpstreamDeliveryOrderCommand"/> minus the
/// <c>SourceSystemKey</c> field — the server pins that from the
/// authenticated <see cref="SystemPrincipal"/> so the wire payload
/// can't lie about its origin.
/// </summary>
public record CreateSourceOrderRequest(
    string OrderRef,
    ServiceWindowDto ServiceWindow,
    List<ItemDto> Items,
    Priority? Priority = null,
    string? RequestedBy = null,
    string? Notes = null,
    TransportMode? RequestedTransportMode = TransportMode.Amr,
    [property: DefaultValue(false)] bool? RequiresDropPod = false,
    [property: DefaultValue(false)] bool? RequiresPickupPod = false,
    // When true, this source system executes the transport itself: DTMS auto
    // acknowledges + picks up the trip (actor = RequestedBy) and the source
    // reports drop + complete via /api/v1/source/trips/*. Manual mode only
    // (RequestedTransportMode=Manual) and RequestedBy is required.
    [property: DefaultValue(false)] bool SelfManaged = false);
