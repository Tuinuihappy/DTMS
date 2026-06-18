using AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.ReplanStuckOrder;
using AMR.DeliveryPlanning.Dispatch.Application.Commands.ForceCompleteTrip;
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

        // Admin override for a Trip stuck at InProgress/Paused because the
        // vendor's TASK_FINISHED webhook was dropped (or never fired). Use
        // when the Riot3 reconciliation poller can't recover the trip from
        // vendor state — e.g. RIOT3 itself never recorded the task as
        // finished, or the trip's upperKey no longer exists in RIOT3. The
        // handler runs the same Trip.MarkVendorCompleted domain transition
        // the webhook would have, including TripCompletedIntegrationEvent
        // cascade to DeliveryOrder + Job.
        group.MapPost("/trips/{id:guid}/force-complete", async (
            Guid id,
            [FromBody] AdminForceCompleteRequest body,
            ISender sender,
            ICurrentActorContext actor) =>
        {
            var triggeredBy = body.TriggeredBy ?? actor.Current.TriggeredBy ?? "admin";
            var reason = body.Reason ?? "manual admin force-complete";

            var result = await sender.Send(new ForceCompleteTripCommand(
                TripId: id,
                Reason: reason,
                TriggeredBy: triggeredBy));

            return result.IsSuccess
                ? Results.Ok()
                : Results.BadRequest(result.Error);
        })
        .WithName("AdminForceCompleteTrip")
        .WithSummary("Force-complete a Trip stuck at InProgress/Paused because TASK_FINISHED webhook was dropped.");
    }
}

public record AdminReplanRequest(string? TriggeredBy, string? Reason);
public record AdminForceCompleteRequest(string? TriggeredBy, string? Reason);
