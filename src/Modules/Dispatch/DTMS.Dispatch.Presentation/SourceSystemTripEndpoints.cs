using DTMS.Dispatch.Application.Commands.SourceSystemTrip;
using DTMS.Iam.Application.Authorization;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
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

        group.MapPost("/{tripId:guid}/acknowledge",
            (Guid tripId, HttpContext ctx, ISender sender) =>
                DispatchAsync(ctx, sender, key => new SourceAcknowledgeTripCommand(tripId, key)))
            .RequirePermissionFromRouteKey(StandardSystemPermissions.TripAcknowledgeTemplate);

        group.MapPost("/{tripId:guid}/pickup",
            (Guid tripId, HttpContext ctx, ISender sender) =>
                DispatchAsync(ctx, sender, key => new SourcePickupTripCommand(tripId, key)))
            .RequirePermissionFromRouteKey(StandardSystemPermissions.TripPickupTemplate);

        group.MapPost("/{tripId:guid}/drop",
            (Guid tripId, HttpContext ctx, ISender sender) =>
                DispatchAsync(ctx, sender, key => new SourceDropTripCommand(tripId, key)))
            .RequirePermissionFromRouteKey(StandardSystemPermissions.TripDropTemplate);

        group.MapPost("/{tripId:guid}/complete",
            (Guid tripId, HttpContext ctx, ISender sender) =>
                DispatchAsync(ctx, sender, key => new SourceCompleteTripCommand(tripId, key)))
            .RequirePermissionFromRouteKey(StandardSystemPermissions.TripCompleteTemplate);
    }

    // Resolves the authenticated system key, builds the action command from it,
    // and maps the Result to HTTP. The principal is guaranteed present by the
    // /api/v1/source auth middleware — a null here is a wiring bug, not input.
    private static async Task<IResult> DispatchAsync<TCommand>(
        HttpContext ctx,
        ISender sender,
        Func<string, TCommand> buildCommand)
        where TCommand : IRequest<DTMS.SharedKernel.Messaging.Result>
    {
        if (ctx.Items["principal"] is not SystemPrincipal principal)
            return Results.Problem(
                title: "system principal missing",
                detail: "SystemClientAuthMiddleware did not run for this request",
                statusCode: StatusCodes.Status500InternalServerError);

        var result = await sender.Send(buildCommand(principal.Key));
        return result.IsSuccess
            ? Results.NoContent()
            : Results.BadRequest(new { error = result.Error });
    }
}
