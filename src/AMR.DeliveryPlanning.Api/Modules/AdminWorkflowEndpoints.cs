using AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.ReplanStuckOrder;
using AMR.DeliveryPlanning.SharedKernel.Auth;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace AMR.DeliveryPlanning.Api.Modules;

/// <summary>
/// T1.7 — admin-only recovery endpoints for the workflow pipeline. Sits next
/// to <see cref="AdminProjectionsEndpoints"/> so all ops surfaces share one
/// route prefix and one auth policy.
///
/// <para><b>/replan</b> vs <b>/redispatch</b>: /redispatch (in the
/// DeliveryOrder module) requires the order to be at Confirmed status —
/// operators have to /reopen first if it's Failed. /replan accepts any
/// in-flight status (Confirmed / Planning / Planned / Dispatched) so a
/// post-restart stuck order (the OD-0374 / OD-0375 incident shape) can be
/// recovered in one HTTP call. Same idempotency story: handler republishes
/// the integration event, Planning consumer is safe to re-run thanks to
/// T1.5 guards.</para>
/// </summary>
public static class AdminWorkflowEndpoints
{
    public static void MapAdminWorkflowEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin")
            .WithTags("Admin")
            .RequireAuthorization();

        group.MapPost("/orders/{id:guid}/replan", async (
            Guid id,
            [FromBody] AdminReplanRequest body,
            ISender sender,
            ICurrentActorContext actor) =>
        {
            var triggeredBy = body.TriggeredBy ?? actor.Current.TriggeredBy ?? "admin";
            var reason = body.Reason ?? "manual admin replay";

            var result = await sender.Send(new ReplanStuckOrderCommand(
                OrderId: id,
                TriggeredBy: triggeredBy,
                Reason: reason,
                RequireStuckPlanned: false));

            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.BadRequest(result.Error);
        })
        .WithName("AdminReplanOrder")
        .WithSummary("Replan a stuck order by republishing DeliveryOrderConfirmedIntegrationEventV1.");
    }
}

public record AdminReplanRequest(string? TriggeredBy, string? Reason);
