using DTMS.DeliveryOrder.Application.Commands.CreateDraftDeliveryOrder;
using DTMS.DeliveryOrder.Application.Commands.CreateUpstreamDeliveryOrder;
using DTMS.DeliveryOrder.Domain.Enums;
using DTMS.DeliveryOrder.Presentation.Idempotency;
using DTMS.Iam.Application.Authorization;
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
        group.MapPost("/delivery-orders", HandleCreateAsync)
            .RequireIdempotencyKey()
            .RequirePermissionFromRouteKey(StandardSystemPermissions.OrderWriteTemplate);
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
            RequiresPickupPod: body.RequiresPickupPod);

        var result = await sender.Send(command);
        return result.IsSuccess
            ? Results.Created(
                $"/api/v1/delivery-orders/{result.Value.Order.Id}",
                result.Value)
            : Results.BadRequest(new { error = result.Error });
    }
}

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
    bool? RequiresDropPod = null,
    bool? RequiresPickupPod = null);
