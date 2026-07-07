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
    }

    // Validates the actor body, resolves the authenticated system key, builds
    // the action command from (key, actionBy, actedAt), and maps the Result to
    // HTTP. The principal is guaranteed present by the /api/v1/source auth
    // middleware — a null here is a wiring bug, not input.
    private static async Task<IResult> DispatchAsync<TCommand>(
        HttpContext ctx,
        ISender sender,
        SourceTripActionRequest? body,
        Func<string, string, DateTime?, TCommand> buildCommand)
        where TCommand : IRequest<DTMS.SharedKernel.Messaging.Result>
    {
        if (ctx.Items["principal"] is not SystemPrincipal principal)
            return Results.Problem(
                title: "system principal missing",
                detail: "SystemClientAuthMiddleware did not run for this request",
                statusCode: StatusCodes.Status500InternalServerError);

        if (string.IsNullOrWhiteSpace(body?.ActionBy))
            return Results.BadRequest(new { error = "actionBy is required." });

        var result = await sender.Send(buildCommand(principal.Key, body.ActionBy, body.ActedAt));
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
