using AMR.DeliveryPlanning.SharedKernel.Messaging;
using AMR.DeliveryPlanning.Transport.Manual.Application.Commands.AcknowledgeTrip;
using AMR.DeliveryPlanning.Transport.Manual.Application.Commands.CompleteTrip;
using AMR.DeliveryPlanning.Transport.Manual.Application.Commands.RecordDrop;
using AMR.DeliveryPlanning.Transport.Manual.Application.Commands.RecordPickup;
using AMR.DeliveryPlanning.Transport.Manual.Application.Commands.RegisterPushSubscription;
using AMR.DeliveryPlanning.Transport.Manual.Application.Commands.SubmitGeofenceOverride;
using AMR.DeliveryPlanning.Transport.Manual.Application.Commands.UnregisterPushSubscription;
using AMR.DeliveryPlanning.Transport.Manual.Application.Queries.GetAssignedTrips;
using AMR.DeliveryPlanning.Transport.Manual.Application.Queries.GetMyProfile;
using AMR.DeliveryPlanning.Transport.Manual.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AMR.DeliveryPlanning.Transport.Manual.Presentation;

// Phase 4.2 — Operator PWA REST API. All endpoints require the
// OperatorOnly policy (which transitively requires the OperatorJwt
// auth scheme); OperatorSyncMiddleware populates HttpContext.Items
// with the DTMS-side Operator.Id so endpoints don't need to re-resolve
// from JWT claims.
public static class OperatorEndpoints
{
    // Mirror of OperatorSyncMiddleware.OperatorIdItemKey — duplicated here
    // because Presentation doesn't reference the Api project.
    private const string OperatorIdItemKey = "Operator:Id";

    // Policy name — also duplicated (Api project defines the actual policy).
    private const string OperatorOnlyPolicy = "OperatorOnly";

    public static void MapOperatorEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/operator")
                       .WithTags("Operator")
                       .RequireAuthorization(OperatorOnlyPolicy);

        // ── Profile + trips ───────────────────────────────────────────
        group.MapGet("/me", async (HttpContext ctx, ISender sender) =>
        {
            var opId = ResolveOperatorId(ctx);
            var result = await sender.Send(new GetMyProfileQuery(opId));
            return ToHttp(result);
        });

        group.MapGet("/trips/assigned", async (HttpContext ctx, ISender sender) =>
        {
            var opId = ResolveOperatorId(ctx);
            var result = await sender.Send(new GetAssignedTripsQuery(opId));
            return ToHttp(result);
        });

        // ── Trip actions ──────────────────────────────────────────────
        group.MapPost("/trips/{tripId:guid}/acknowledge",
            async (Guid tripId, HttpContext ctx, ISender sender) =>
            {
                var opId = ResolveOperatorId(ctx);
                var result = await sender.Send(new AcknowledgeTripCommand(tripId, opId));
                return ToHttp(result);
            });

        group.MapPost("/trips/{tripId:guid}/pickup",
            async (Guid tripId, RecordPickupRequest req, HttpContext ctx, ISender sender) =>
            {
                var opId = ResolveOperatorId(ctx);
                var result = await sender.Send(new RecordPickupCommand(
                    tripId, opId, req.Lat, req.Lng, req.PodKey));
                return ToHttp(result);
            });

        group.MapPost("/trips/{tripId:guid}/drop",
            async (Guid tripId, RecordDropRequest req, HttpContext ctx, ISender sender) =>
            {
                var opId = ResolveOperatorId(ctx);
                var result = await sender.Send(new RecordDropCommand(
                    tripId, opId, req.Lat, req.Lng, req.PodKey));
                return ToHttp(result);
            });

        group.MapPost("/trips/{tripId:guid}/complete",
            async (Guid tripId, HttpContext ctx, ISender sender) =>
            {
                var opId = ResolveOperatorId(ctx);
                var result = await sender.Send(new CompleteTripCommand(tripId, opId));
                return ToHttp(result);
            });

        // ── Geofence override ─────────────────────────────────────────
        group.MapPost("/geofence/override-request",
            async (SubmitOverrideRequest req, HttpContext ctx, ISender sender) =>
            {
                var opId = ResolveOperatorId(ctx);
                var result = await sender.Send(new SubmitGeofenceOverrideCommand(
                    req.TripId, opId, req.ExpectedWarehouseId,
                    req.Lat, req.Lng, req.Reason, req.PhotoUrl));
                return result.IsSuccess
                    ? Results.Created($"/api/operator/geofence/override-request/{result.Value}", new { Id = result.Value })
                    : Results.BadRequest(new { Error = result.Error });
            });

        // ── Push subscriptions ────────────────────────────────────────
        group.MapPost("/devices/register-push",
            async (RegisterPushRequest req, HttpContext ctx, ISender sender) =>
            {
                var opId = ResolveOperatorId(ctx);
                var platform = Enum.TryParse<PushPlatform>(req.Platform, ignoreCase: true, out var p)
                    ? p : PushPlatform.WebPush;
                var result = await sender.Send(new RegisterPushSubscriptionCommand(
                    opId, platform, req.Endpoint, req.PublicKey, req.AuthSecret, req.DeviceLabel));
                return ToHttp(result);
            });

        group.MapDelete("/devices/push",
            async (string endpoint, HttpContext ctx, ISender sender) =>
            {
                var opId = ResolveOperatorId(ctx);
                var result = await sender.Send(new UnregisterPushSubscriptionCommand(opId, endpoint));
                return ToHttp(result);
            });

        // ── POD presign (stub) ────────────────────────────────────────
        // Phase 4.3 will wire the real MinIO presign here. For 4.2 we
        // return a placeholder so the operator PWA can be wired without
        // failing the call — it'll receive a deliberately-broken URL
        // that logs a clear "Phase 4.3 not yet shipped" hint.
        group.MapPost("/pod/presign",
            (PresignRequest req, HttpContext ctx) =>
            {
                _ = ResolveOperatorId(ctx);
                return Results.Ok(new
                {
                    UploadUrl = $"https://phase-4-3-not-shipped.invalid/pod/{Guid.NewGuid()}",
                    ObjectKey = $"pod/{req.TripId}/{Guid.NewGuid()}",
                    ExpiresAt = DateTime.UtcNow.AddMinutes(15),
                    Note = "Stub — Phase 4.3 will replace this with a real MinIO presigned PUT URL.",
                });
            });
    }

    private static Guid ResolveOperatorId(HttpContext ctx)
    {
        if (ctx.Items.TryGetValue(OperatorIdItemKey, out var v) && v is Guid id)
            return id;
        throw new InvalidOperationException(
            "Operator Id missing — OperatorSyncMiddleware did not run for this request.");
    }

    private static IResult ToHttp(Result result) =>
        result.IsSuccess ? Results.NoContent() : Results.BadRequest(new { Error = result.Error });

    private static IResult ToHttp<T>(Result<T> result) =>
        result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(new { Error = result.Error });
}

// ── Request DTOs ──────────────────────────────────────────────────────
public record RecordPickupRequest(double Lat, double Lng, string? PodKey);
public record RecordDropRequest(double Lat, double Lng, string? PodKey);
public record SubmitOverrideRequest(
    Guid TripId, Guid ExpectedWarehouseId, double Lat, double Lng,
    string Reason, string? PhotoUrl);
public record RegisterPushRequest(
    string Platform, string Endpoint, string? PublicKey, string? AuthSecret, string? DeviceLabel);
public record PresignRequest(Guid TripId, string Kind);
