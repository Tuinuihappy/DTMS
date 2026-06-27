using AMR.DeliveryPlanning.Api.Realtime.Drain;
using DTMS.DeliveryOrder.Application.Commands.ReplanStuckOrder;
using AMR.DeliveryPlanning.Dispatch.Application.Commands.ForceCompleteTrip;
using AMR.DeliveryPlanning.Dispatch.Application.Commands.ForceDropCompletedTrip;
using AMR.DeliveryPlanning.Dispatch.Application.Commands.ForcePickupCompletedTrip;
using AMR.DeliveryPlanning.Dispatch.Application.Commands.ForceStartTrip;
using DTMS.SharedKernel.Auth;
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

        // Four admin overrides mirroring the four RIOT3 webhooks DTMS
        // consumes for envelope-dispatched trips. Each runs the same domain
        // transition the webhook would have, so the downstream cascade
        // (integration events → consumers → projections → upstream OMS)
        // fires exactly once and in the same shape. Upstream-OMS reach:
        //
        //   start   → POST /shipments  (via TripStartedOmsNotifyConsumer)
        //   pickup  → in-DTMS only     (item state Pending → Picked)
        //   drop    → POST /arrived    (via TripDropCompletedOmsNotifyConsumer)
        //   complete→ in-DTMS only     (cascades to Order + Job terminal state)

        group.MapPost("/trips/{id:guid}/force-start", async (
            Guid id,
            [FromBody] AdminForceStartRequest body,
            ISender sender,
            ICurrentActorContext actor) =>
        {
            var triggeredBy = body.TriggeredBy ?? actor.Current.TriggeredBy ?? "admin";
            var reason = body.Reason ?? "manual admin force-start";

            var result = await sender.Send(new ForceStartTripCommand(
                TripId: id,
                Reason: reason,
                TriggeredBy: triggeredBy,
                VendorVehicleKey: body.VendorVehicleKey,
                VendorVehicleName: body.VendorVehicleName));

            return result.IsSuccess ? Results.Ok() : Results.BadRequest(result.Error);
        })
        .WithName("AdminForceStartTrip")
        .WithSummary("Force-start a Trip stuck at Created because TASK_PROCESSING webhook was dropped. Notifies upstream OMS.");

        group.MapPost("/trips/{id:guid}/force-pickup-completed", async (
            Guid id,
            [FromBody] AdminForceStageRequest body,
            ISender sender,
            ICurrentActorContext actor) =>
        {
            var triggeredBy = body.TriggeredBy ?? actor.Current.TriggeredBy ?? "admin";
            var reason = body.Reason ?? "manual admin force-pickup";

            var result = await sender.Send(new ForcePickupCompletedTripCommand(
                TripId: id,
                Reason: reason,
                TriggeredBy: triggeredBy));

            return result.IsSuccess ? Results.Ok() : Results.BadRequest(result.Error);
        })
        .WithName("AdminForcePickupCompletedTrip")
        .WithSummary("Force-pickup-complete a Trip whose pickup sub-task webhook was dropped. Does NOT notify upstream OMS.");

        group.MapPost("/trips/{id:guid}/force-drop-completed", async (
            Guid id,
            [FromBody] AdminForceStageRequest body,
            ISender sender,
            ICurrentActorContext actor) =>
        {
            var triggeredBy = body.TriggeredBy ?? actor.Current.TriggeredBy ?? "admin";
            var reason = body.Reason ?? "manual admin force-drop";

            var result = await sender.Send(new ForceDropCompletedTripCommand(
                TripId: id,
                Reason: reason,
                TriggeredBy: triggeredBy));

            return result.IsSuccess ? Results.Ok() : Results.BadRequest(result.Error);
        })
        .WithName("AdminForceDropCompletedTrip")
        .WithSummary("Force-drop-complete a Trip whose drop sub-task webhook was dropped. Notifies upstream OMS.");

        group.MapPost("/trips/{id:guid}/force-complete", async (
            Guid id,
            [FromBody] AdminForceStageRequest body,
            ISender sender,
            ICurrentActorContext actor) =>
        {
            var triggeredBy = body.TriggeredBy ?? actor.Current.TriggeredBy ?? "admin";
            var reason = body.Reason ?? "manual admin force-complete";

            var result = await sender.Send(new ForceCompleteTripCommand(
                TripId: id,
                Reason: reason,
                TriggeredBy: triggeredBy));

            return result.IsSuccess ? Results.Ok() : Results.BadRequest(result.Error);
        })
        .WithName("AdminForceCompleteTrip")
        .WithSummary("Force-complete a Trip stuck at InProgress/Paused because TASK_FINISHED webhook was dropped. Does NOT notify upstream OMS.");

        // G1 Phase 1 — pod drain. Called by K8s preStop hook (curl from
        // inside the pod) or operators during planned shutdown. Returns 202
        // immediately after kicking off the drain in a background task —
        // the kubelet's preStop is synchronous and would block the whole
        // shutdown until either Task.Delay(settleWindow) returns or
        // terminationGracePeriodSeconds expires. We don't want the HTTP
        // call to hold; the K8s shutdown sequence already waits the grace
        // period. Idempotent on the service side, so duplicate calls from
        // a retried preStop hook are safe.
        //
        // Auth: bypasses the group's RequireAuthorization() because the
        // K8s preStop hook runs without a JWT. Gated by a loopback-only
        // check on the request's RemoteIpAddress — a drain can only be
        // triggered from inside the pod (preStop, kubectl exec, sidecar).
        // External callers get a 403. In prod a NetworkPolicy would be
        // belt-and-suspenders, but the loopback check stands alone.
        group.MapPost("/drain-start", (
            HttpContext ctx,
            [FromBody] AdminDrainStartRequest? body,
            IConnectionDrainService drain,
            ILogger<ConnectionDrainService> logger) =>
        {
            var remote = ctx.Connection.RemoteIpAddress;
            if (remote is null || !System.Net.IPAddress.IsLoopback(remote))
            {
                logger.LogWarning("[Drain] /drain-start refused — non-loopback caller {Remote}", remote);
                return Results.Forbid();
            }

            var settleSeconds = body?.SettleSeconds ?? 45;
            var settle = TimeSpan.FromSeconds(Math.Clamp(settleSeconds, 1, 300));

            if (drain.IsDraining)
            {
                return Results.Ok(new AdminDrainStartResponse(
                    Status: "already-draining",
                    StartedAt: drain.StartedAt,
                    SettleSeconds: settle.TotalSeconds));
            }

            // Fire-and-forget: the drain runs in the background so the HTTP
            // response returns immediately. Exceptions are logged inside
            // ConnectionDrainService; nothing here to await.
            _ = Task.Run(async () =>
            {
                try { await drain.StartDrainAsync(settle); }
                catch (Exception ex) { logger.LogError(ex, "[Drain] background drain failed"); }
            });

            return Results.Accepted(value: new AdminDrainStartResponse(
                Status: "draining",
                StartedAt: DateTimeOffset.UtcNow,
                SettleSeconds: settle.TotalSeconds));
        })
        .AllowAnonymous()
        .WithName("AdminDrainStart")
        .WithSummary("Begin pod drain — flips /health/ready to 503 + broadcasts __drain to all hubs + waits settle window. Loopback-only.");
    }
}

public record AdminReplanRequest(string? TriggeredBy, string? Reason);
public record AdminForceStageRequest(string? TriggeredBy, string? Reason);
public record AdminForceStartRequest(
    string? TriggeredBy,
    string? Reason,
    string? VendorVehicleKey,
    string? VendorVehicleName);
public record AdminDrainStartRequest(int? SettleSeconds);
public record AdminDrainStartResponse(string Status, DateTimeOffset? StartedAt, double SettleSeconds);
