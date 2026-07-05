using DTMS.Iam.Application.Authorization;
using DTMS.SharedKernel.Messaging;
using DTMS.Transport.Manual.Application.Commands.AcknowledgeTrip;
using DTMS.Transport.Manual.Application.Commands.CompleteTrip;
using DTMS.Transport.Manual.Application.Commands.RecordDrop;
using DTMS.Transport.Manual.Application.Commands.RecordPickup;
using DTMS.Transport.Manual.Application.Commands.RegisterPushSubscription;
using DTMS.Transport.Manual.Application.Commands.SubmitGeofenceOverride;
using DTMS.Transport.Manual.Application.Commands.UnregisterPushSubscription;
using DTMS.Transport.Manual.Application.Queries.GetAssignedTrips;
using DTMS.Transport.Manual.Application.Queries.GetMyProfile;
using DTMS.Transport.Manual.Application.Queries.GetPodPresignedUrl;
using DTMS.Transport.Manual.Application.Queries.GetPoolTrips;
using DTMS.Transport.Manual.Application.Services;
using DTMS.Transport.Manual.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DTMS.Transport.Manual.Presentation;

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
        }).RequirePermission(Permissions.Operator.ProfileRead);

        group.MapGet("/trips/assigned", async (HttpContext ctx, ISender sender) =>
        {
            var opId = ResolveOperatorId(ctx);
            var result = await sender.Send(new GetAssignedTripsQuery(opId));
            return ToHttp(result);
        }).RequirePermission(Permissions.Operator.ProfileRead);

        // ── Trip actions ──────────────────────────────────────────────
        // WMS PR-4b (PR-D) — Pool list. Universal visibility (no zone or
        // warehouse filter); FIFO by DispatchedAt. Frontend renders cards +
        // subscribes to /hubs/operator-pool for realtime add/claim/remove.
        group.MapGet("/trips/pool",
            async (ISender sender) =>
            {
                var result = await sender.Send(new GetPoolTripsQuery());
                return ToHttp(result);
                // Reuse the acknowledge permission — anyone who can see the
                // pool must also be able to act on it (there's no read-only
                // pool viewer role today).
            }).RequirePermission(Permissions.Operator.TripAcknowledge);

        group.MapPost("/trips/{tripId:guid}/acknowledge",
            async (Guid tripId, HttpContext ctx, ISender sender) =>
            {
                var opId = ResolveOperatorId(ctx);
                var result = await sender.Send(new AcknowledgeTripCommand(tripId, opId));
                // WMS PR-4b — pool race handling. When two operators tap
                // Acknowledge on the same pooled trip, exactly one wins the
                // SQL CAS; the loser gets AlreadyClaimedErrorCode so the
                // PWA can toast "someone else took it" and refresh the pool
                // list (instead of a generic 400 that reads like user error).
                if (result.IsFailure && result.Error == AcknowledgeTripErrorCodes.AlreadyClaimed)
                    return Results.Conflict(new { Error = result.Error });
                return ToHttp(result);
            }).RequirePermission(Permissions.Operator.TripAcknowledge);

        group.MapPost("/trips/{tripId:guid}/pickup",
            async (Guid tripId, RecordPickupRequest req, HttpContext ctx, ISender sender) =>
            {
                var opId = ResolveOperatorId(ctx);
                var result = await sender.Send(new RecordPickupCommand(
                    tripId, opId, req.Lat, req.Lng, req.PodKey));
                return ToHttp(result);
            }).RequirePermission(Permissions.Operator.TripPickup);

        group.MapPost("/trips/{tripId:guid}/drop",
            async (Guid tripId, RecordDropRequest req, HttpContext ctx, ISender sender) =>
            {
                var opId = ResolveOperatorId(ctx);
                var result = await sender.Send(new RecordDropCommand(
                    tripId, opId, req.Lat, req.Lng, req.PodKey));
                return ToHttp(result);
            }).RequirePermission(Permissions.Operator.TripDrop);

        group.MapPost("/trips/{tripId:guid}/complete",
            async (Guid tripId, HttpContext ctx, ISender sender) =>
            {
                var opId = ResolveOperatorId(ctx);
                var result = await sender.Send(new CompleteTripCommand(tripId, opId));
                return ToHttp(result);
            }).RequirePermission(Permissions.Operator.TripComplete);

        // ── Geofence override ─────────────────────────────────────────
        group.MapPost("/geofence/override-request",
            async (SubmitOverrideRequest req, HttpContext ctx, ISender sender) =>
            {
                var opId = ResolveOperatorId(ctx);
                var result = await sender.Send(new SubmitGeofenceOverrideCommand(
                    req.TripId, opId, req.ExpectedWmsLocationId,
                    req.Lat, req.Lng, req.Reason, req.PhotoUrl));
                return result.IsSuccess
                    ? Results.Created($"/api/operator/geofence/override-request/{result.Value}", new { Id = result.Value })
                    : Results.BadRequest(new { Error = result.Error });
            }).RequirePermission(Permissions.Operator.GeofenceOverride);

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
            }).RequirePermission(Permissions.Operator.PushRegister);

        group.MapDelete("/devices/push",
            async (string endpoint, HttpContext ctx, ISender sender) =>
            {
                var opId = ResolveOperatorId(ctx);
                var result = await sender.Send(new UnregisterPushSubscriptionCommand(opId, endpoint));
                return ToHttp(result);
            }).RequirePermission(Permissions.Operator.PushRegister);

        // ── Web Push (per ADR-013) ────────────────────────────────────
        // Public key feed for the PWA's SW.subscribe() call. Not
        // sensitive; anonymous on purpose so a fresh PWA install can
        // bootstrap without a token.
        app.MapGet("/api/operator/push/vapid-public-key",
                (IVapidPublicKeyProvider provider) =>
                    Results.Ok(new { PublicKey = provider.PublicKey }))
            .AllowAnonymous()
            .WithTags("Operator");

        // Self-test — operator triggers a push to themselves to verify
        // their device registration works. Useful first-run smoke from
        // the PWA's Settings page.
        group.MapPost("/push/test",
            async (HttpContext ctx, IPushNotificationGateway push) =>
            {
                var opId = ResolveOperatorId(ctx);
                var outcome = await push.SendToOperatorAsync(opId, new PushNotificationPayload(
                    Title: "DTMS test",
                    Body: "If you see this, push notifications are working.",
                    Url: "/m/trips",
                    Tag: "dtms-test"));
                return Results.Ok(outcome);
            }).RequirePermission(Permissions.Operator.PushRegister);

        // ── POD presign (MinIO, per ADR-015) ──────────────────────────
        group.MapPost("/pod/presign",
            async (PresignRequest req, HttpContext ctx, ISender sender) =>
            {
                var opId = ResolveOperatorId(ctx);
                var result = await sender.Send(new GetPodPresignedUrlQuery(
                    TripId: req.TripId,
                    OperatorId: opId,
                    Kind: req.Kind,
                    FileExtension: req.FileExtension ?? "jpg"));
                return ToHttp(result);
            }).RequirePermission(Permissions.Operator.PodUpload);
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
    Guid TripId, Guid ExpectedWmsLocationId, double Lat, double Lng,
    string Reason, string? PhotoUrl);
public record RegisterPushRequest(
    string Platform, string Endpoint, string? PublicKey, string? AuthSecret, string? DeviceLabel);
public record PresignRequest(Guid TripId, string Kind, string? FileExtension = "jpg");
