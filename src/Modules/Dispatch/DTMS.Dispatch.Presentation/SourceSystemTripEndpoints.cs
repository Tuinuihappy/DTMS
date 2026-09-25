using DTMS.Dispatch.Application.Commands.SourceSystemTrip;
using DTMS.Iam.Application.Authorization;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace DTMS.Dispatch.Presentation;

/// <summary>
/// Federated source-system trip-lifecycle endpoints. Mounted under
/// <c>/api/v1/source/*</c> so <c>SystemClientAuthMiddleware</c> authenticates
/// the machine caller (OAuth 2.0 client_credentials → bearer JWT) and stamps
/// a <see cref="SystemPrincipal"/> before these run — the exact same pipeline
/// as <see cref="SourceSystemDeliveryOrderEndpoints"/>.
///
/// <para>These are the machine-caller analogue of the operator PWA trip
/// actions (<c>/api/operator/trips/*</c>): a source system reports its trips'
/// progress instead of a human tapping the PWA. Both converge on the same
/// idempotent <c>Trip.MarkVendor*</c> domain methods; the difference is the
/// auth model (system JWT + per-action permission + order-origin match) and
/// the absence of geofence/POD (the system is trusted, like the AMR webhook).</para>
///
/// <para><b>Origin safety.</b> A <c>Trip</c> has no source-system key, so each
/// command re-derives ownership from the parent order's <c>SourceSystemKey</c>
/// and rejects any caller acting on a trip it doesn't own. The
/// <c>SourceSystemKey</c> handed to the command is pinned from the
/// authenticated principal, never the request body.</para>
/// </summary>
public static class SourceSystemTripEndpoints
{
    public static void MapSourceSystemTripEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/source/trips").WithTags("SourceSystem");

        // All four actions take the same body — WHO performed the action in the
        // caller's own system and WHEN. SourceSystemKey is always pinned from
        // the authenticated principal, never the body, so origin can't be spoofed.
        group.MapPost("/{tripId:guid}/acknowledge",
            (Guid tripId, [FromBody] SourceTripActionRequest body, HttpContext ctx, ISender sender) =>
                DispatchAsync(ctx, sender, body, (key, by, at) =>
                    new SourceAcknowledgeTripCommand(tripId, key, by, at)))
            .RequirePermissionFromRouteKey(StandardSystemPermissions.TripAcknowledgeTemplate);

        group.MapPost("/{tripId:guid}/pickup",
            (Guid tripId, [FromBody] SourceTripActionRequest body, HttpContext ctx, ISender sender) =>
                DispatchAsync(ctx, sender, body, (key, by, at) =>
                    new SourcePickupTripCommand(tripId, key, by, at)))
            .RequirePermissionFromRouteKey(StandardSystemPermissions.TripPickupTemplate);

        group.MapPost("/{tripId:guid}/drop",
            (Guid tripId, [FromBody] SourceTripActionRequest body, HttpContext ctx, ISender sender) =>
                DispatchAsync(ctx, sender, body, (key, by, at) =>
                    new SourceDropTripCommand(tripId, key, by, at)))
            .RequirePermissionFromRouteKey(StandardSystemPermissions.TripDropTemplate);

        group.MapPost("/{tripId:guid}/complete",
            (Guid tripId, [FromBody] SourceTripActionRequest body, HttpContext ctx, ISender sender) =>
                DispatchAsync(ctx, sender, body, (key, by, at) =>
                    new SourceCompleteTripCommand(tripId, key, by, at)))
            .RequirePermissionFromRouteKey(StandardSystemPermissions.TripCompleteTemplate);

        // Robot PASS — a source system acting as remote operator nudges a
        // DTMS-executed AMR robot past a checkpoint. Unlike the four actions
        // above (self-managed progress reporting) this only applies to AMR
        // trips; the command returns a 400 for non-AMR / non-InProgress trips.
        // Same body (WHO/WHEN); origin still pinned + verified server-side.
        group.MapPost("/{tripId:guid}/acknowledge-robot-pass",
            (Guid tripId, [FromBody] SourceTripActionRequest body, HttpContext ctx, ISender sender) =>
                DispatchAsync(ctx, sender, body, (key, by, at) =>
                    new SourceAcknowledgeRobotPassCommand(tripId, key, by, at)))
            .RequirePermissionFromRouteKey(StandardSystemPermissions.TripAcknowledgeRobotPassTemplate);

        // Cancel — the destructive one. Unlike the five above it is not
        // self-managed-only: a DTMS-executed AMR trip is in scope, so this
        // reaches RIOT3 with a real CMD_ORDER_CANCEL whenever a VendorOrderKey
        // exists. A repeat cancel of an already-cancelled trip answers 204
        // rather than 400 (see the handler), and on a single-trip order the b11
        // cascade will carry the parent DeliveryOrder to Cancelled with it.
        group.MapPost("/{tripId:guid}/cancel",
            (Guid tripId, [FromBody] SourceTripCancelRequest body, HttpContext ctx, ISender sender) =>
                DispatchCancelAsync(ctx, sender, body, (key, by, reason, at) =>
                    new SourceCancelTripCommand(tripId, key, by, reason, at)))
            .RequirePermissionFromRouteKey(StandardSystemPermissions.TripCancelTemplate);
    }

    // Cancel carries a field the other five don't (Reason), so it gets its own
    // body record and its own adapter. Both adapters funnel into
    // DispatchCoreAsync, so principal resolution, the actionBy guard and the
    // Result→HTTP mapping stay written once.
    private static Task<IResult> DispatchAsync<TCommand>(
        HttpContext ctx,
        ISender sender,
        SourceTripActionRequest? body,
        Func<string, string, DateTime?, TCommand> buildCommand)
        where TCommand : IRequest<DTMS.SharedKernel.Messaging.Result>
        => DispatchCoreAsync(ctx, sender, body?.ActionBy,
               key => buildCommand(key, body!.ActionBy, body.ActedAt));

    private static Task<IResult> DispatchCancelAsync<TCommand>(
        HttpContext ctx,
        ISender sender,
        SourceTripCancelRequest? body,
        Func<string, string, string?, DateTime?, TCommand> buildCommand)
        where TCommand : IRequest<DTMS.SharedKernel.Messaging.Result>
        => DispatchCoreAsync(ctx, sender, body?.ActionBy,
               key => buildCommand(key, body!.ActionBy, body.Reason, body.ActedAt));

    // Validates the actor, resolves the authenticated system key, builds the
    // command from it, and maps the Result to HTTP. The principal is guaranteed
    // present by the /api/v1/source auth middleware — a null here is a wiring
    // bug, not input. buildCommand only runs after the actionBy guard, which is
    // what makes the `body!` inside each adapter safe.
    private static async Task<IResult> DispatchCoreAsync<TCommand>(
        HttpContext ctx,
        ISender sender,
        string? actionBy,
        Func<string, TCommand> buildCommand)
        where TCommand : IRequest<DTMS.SharedKernel.Messaging.Result>
    {
        if (ctx.Items["principal"] is not SystemPrincipal principal)
            return Results.Problem(
                title: "system principal missing",
                detail: "SystemClientAuthMiddleware did not run for this request",
                statusCode: StatusCodes.Status500InternalServerError);

        if (string.IsNullOrWhiteSpace(actionBy))
            return Results.BadRequest(new { error = "actionBy is required." });

        var result = await sender.Send(buildCommand(principal.Key));
        return result.IsSuccess
            ? Results.NoContent()
            : Results.BadRequest(new { error = result.Error });
    }
}

/// <summary>
/// Shared body for the four POST /api/v1/source/trips/{tripId}/* lifecycle
/// actions (acknowledge / pickup / drop / complete). The source system forwards
/// WHO performed the action in its own system — a free-text identifier (user id
/// / name / email), because a system-to-system call has no DTMS user — and
/// optionally WHEN it happened upstream. Both land on the trip's ExecutionEvent
/// audit trail. The trip's origin is pinned server-side from the JWT, never
/// from this body.
/// </summary>
/// <param name="ActionBy">Required. Source-system identifier of the human who
/// performed the action.</param>
/// <param name="ActedAt">Optional. When the human performed the action upstream;
/// the server falls back to the receive time when omitted.</param>
public record SourceTripActionRequest(
    string ActionBy,
    DateTime? ActedAt = null);

/// <summary>
/// Body for POST /api/v1/source/trips/{tripId}/cancel — the same WHO/WHEN as
/// <see cref="SourceTripActionRequest"/>, plus an optional WHY.
/// </summary>
/// <param name="ActionBy">Required. Source-system identifier of the human who
/// cancelled. Recorded as the ExecutionEvent's actor.</param>
/// <param name="Reason">Optional, but please send one. It becomes
/// <c>TripCancelledIntegrationEvent.Reason</c> — the line an operator reads in
/// the order activity timeline, and the reason that reaches the source system
/// itself on shipment.cancelled.v1 once that subscription is enabled. Omitting
/// it falls back to a generic label and logs a warning.</param>
/// <param name="ActedAt">Optional. When they cancelled it upstream; the server
/// falls back to the receive time.</param>
public record SourceTripCancelRequest(
    string ActionBy,
    string? Reason = null,
    DateTime? ActedAt = null);
