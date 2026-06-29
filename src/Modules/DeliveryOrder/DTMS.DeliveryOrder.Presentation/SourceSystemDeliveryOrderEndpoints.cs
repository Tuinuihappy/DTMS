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
/// Phase S.2.2 — federated source-system endpoint group. Mirrors the
/// admin-side <c>POST /api/v1/delivery-orders/upstream</c> shape but
/// runs under <c>/api/v1/source/{key}/*</c> so the
/// <see cref="DTMS.Api.Middlewares"/> system auth + request log
/// middleware applies. Routes the request through the same
/// <see cref="CreateUpstreamDeliveryOrderCommand"/> handler — there's
/// only one validated path to "Submitted → Validated → Confirmed",
/// reachable through either the user surface or the system surface.
/// </summary>
public static class SourceSystemDeliveryOrderEndpoints
{
    public static void MapSourceSystemDeliveryOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/source/{key}").WithTags("SourceSystem");

        // POST /api/v1/source/{key}/delivery-orders
        // Authorization: ApiKey ...  (or future bearer-jwt / hmac via the
        //   middleware's auth scheme switch)
        // Idempotency-Key: <uuid>    (transport-level retry guard)
        //
        // Server reconstructs the SourceSystem enum from {key} so the
        // body cannot lie about which system it claims to be — even if
        // an attacker crafted a body with SourceSystem=Sap, the URL +
        // verified credential pin the value to OMS.
        group.MapPost("/delivery-orders",
            async (string key, [FromBody] CreateSourceOrderRequest body, ISender sender) =>
        {
            if (!TryMapSourceSystem(key, out var sourceSystem))
                return Results.BadRequest(new { error = $"Unsupported source system: '{key}'" });

            var command = new CreateUpstreamDeliveryOrderCommand(
                OrderRef: body.OrderRef,
                ServiceWindow: body.ServiceWindow,
                Items: body.Items,
                SourceSystem: sourceSystem,
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
        })
            .RequireIdempotencyKey()
            // Phase S.3.1a — permission code derives from the URL {key}
            // segment at enforcement time, so adding a new SystemClient
            // (sap, erp, wms-acme, ...) requires no code change here —
            // only the matching permission row in iam.SystemClientPermissions
            // and the corresponding credential. Handler validates the slug
            // before substitution to prevent permission-string injection.
            .RequirePermissionFromRouteKey(StandardSystemPermissions.OrderWriteTemplate);
    }

    private static bool TryMapSourceSystem(string urlKey, out SourceSystem mapped)
    {
        // URL key is the slug from iam.SystemClients (lowercase). The
        // domain enum predates the dynamic client table and stays
        // closed by design — a system that isn't in the enum can't
        // create orders even if its credential is valid.
        switch (urlKey)
        {
            case "oms": mapped = SourceSystem.Oms; return true;
            case "sap": mapped = SourceSystem.Sap; return true;
            case "erp": mapped = SourceSystem.Erp; return true;
            default:
                mapped = default;
                return false;
        }
    }
}

/// <summary>
/// Inbound payload shape for source-system order creation. Mirrors
/// <see cref="CreateUpstreamDeliveryOrderCommand"/> minus the
/// <c>SourceSystem</c> field — the server pins that from the URL
/// segment so the wire payload can't lie about its origin.
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
