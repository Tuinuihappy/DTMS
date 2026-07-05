using DTMS.Iam.Application.Authorization;
using DTMS.Iam.Application.Callbacks;
using DTMS.Iam.Application.Repositories;
using DTMS.Iam.Domain.Entities;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DTMS.Iam.Presentation;

/// <summary>
/// Phase S.3.1b — admin surface for outbound callback subscriptions.
/// All endpoints mounted under <c>/api/v1/iam/systems/{key}/subscriptions</c>
/// (matches the existing <c>/api/v1/iam/*</c> admin layout). Mutations
/// audit + evict the <see cref="ISubscriptionLookup"/> cache so the
/// next fan-out consumer call sees the change immediately on this pod
/// and within one Redis round-trip on every other pod.
/// </summary>
public static class SystemSubscriptionEndpoints
{
    public static void MapSystemSubscriptionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/iam")
            .WithTags("IamSubscriptions")
            .RequireAuthorization();

        // ── Read-only: list event types the platform supports ──────────
        group.MapGet("/event-types", () => Results.Ok(CallbackEventTypes.All))
            .RequirePermission(Permissions.Iam.SubscriptionRead);

        // ── List subscriptions for a system ────────────────────────────
        group.MapGet("/systems/{key}/subscriptions",
            async (string key,
                   ISystemClientRepository systems,
                   ISystemEventSubscriptionRepository subs,
                   CancellationToken ct) =>
        {
            if (await systems.GetByKeyAsync(key, ct) is null)
                return Results.NotFound();

            var rows = await subs.ListBySystemAsync(key, ct);
            return Results.Ok(rows.Select(SubscriptionDto.FromEntity));
        }).RequirePermission(Permissions.Iam.SubscriptionRead);

        // ── Create ─────────────────────────────────────────────────────
        group.MapPost("/systems/{key}/subscriptions",
            async (string key,
                   CreateSubscriptionRequest req,
                   HttpContext ctx,
                   ISystemClientRepository systems,
                   ISystemEventSubscriptionRepository subs,
                   IAuditLogRepository audit,
                   ISubscriptionLookup lookup,
                   CancellationToken ct) =>
        {
            if (await systems.GetByKeyAsync(key, ct) is null)
                return Results.NotFound(new { error = $"Unknown system '{key}'." });

            if (!CallbackEventTypes.IsKnown(req.EventType))
                return Results.BadRequest(new
                {
                    error = $"Unknown event type '{req.EventType}'. " +
                            $"Allowed: {string.Join(", ", CallbackEventTypes.All)}"
                });

            if (await subs.GetAsync(key, req.EventType, ct) is not null)
                return Results.Conflict(new
                {
                    error = $"System '{key}' already subscribes to '{req.EventType}'."
                });

            try
            {
                var entity = new SystemEventSubscription(
                    id: Guid.NewGuid(),
                    systemKey: key,
                    eventType: req.EventType,
                    payloadFormatKey: req.PayloadFormatKey,
                    enabled: req.Enabled ?? true);
                await subs.AddAsync(entity, ct);

                await audit.AppendAsync(new PermissionAuditEntry(
                    actorEmployeeId: ActorOrUnknown(ctx),
                    action: "subscription-created",
                    permissionCode: null,
                    details: System.Text.Json.JsonSerializer.Serialize(new
                    {
                        systemKey = key,
                        eventType = req.EventType,
                        payloadFormatKey = req.PayloadFormatKey,
                        enabled = entity.Enabled,
                    })), ct);

                lookup.Invalidate(req.EventType);

                return Results.Created(
                    $"/api/v1/iam/systems/{key}/subscriptions/{req.EventType}",
                    SubscriptionDto.FromEntity(entity));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        }).RequirePermission(Permissions.Iam.SubscriptionWrite);

        // ── Toggle Enabled / change formatter ──────────────────────────
        group.MapPatch("/systems/{key}/subscriptions/{eventType}",
            async (string key, string eventType,
                   PatchSubscriptionRequest req,
                   HttpContext ctx,
                   ISystemEventSubscriptionRepository subs,
                   IAuditLogRepository audit,
                   ISubscriptionLookup lookup,
                   CancellationToken ct) =>
        {
            var row = await subs.GetAsync(key, eventType, ct);
            if (row is null) return Results.NotFound();

            if (req.Enabled is bool e)
            {
                if (e) row.Enable(); else row.Disable();
            }
            if (req.PayloadFormatKey is { Length: > 0 } pfk)
                row.UpdatePayloadFormatKey(pfk);

            await subs.UpdateAsync(row, ct);

            await audit.AppendAsync(new PermissionAuditEntry(
                actorEmployeeId: ActorOrUnknown(ctx),
                action: "subscription-updated",
                permissionCode: null,
                details: System.Text.Json.JsonSerializer.Serialize(new
                {
                    systemKey = key,
                    eventType,
                    enabled = row.Enabled,
                    payloadFormatKey = row.PayloadFormatKey,
                })), ct);

            lookup.Invalidate(eventType);

            return Results.Ok(SubscriptionDto.FromEntity(row));
        }).RequirePermission(Permissions.Iam.SubscriptionWrite);

        // ── Delete ─────────────────────────────────────────────────────
        group.MapDelete("/systems/{key}/subscriptions/{eventType}",
            async (string key, string eventType,
                   HttpContext ctx,
                   ISystemEventSubscriptionRepository subs,
                   IAuditLogRepository audit,
                   ISubscriptionLookup lookup,
                   CancellationToken ct) =>
        {
            var row = await subs.GetAsync(key, eventType, ct);
            if (row is null) return Results.NotFound();

            await subs.RemoveAsync(row, ct);

            await audit.AppendAsync(new PermissionAuditEntry(
                actorEmployeeId: ActorOrUnknown(ctx),
                action: "subscription-deleted",
                permissionCode: null,
                details: System.Text.Json.JsonSerializer.Serialize(new
                {
                    systemKey = key,
                    eventType,
                })), ct);

            lookup.Invalidate(eventType);

            return Results.NoContent();
        }).RequirePermission(Permissions.Iam.SubscriptionWrite);
    }

    private static string ActorOrUnknown(HttpContext ctx)
        => ctx.User.FindFirst("sub")?.Value
           ?? ctx.User.Identity?.Name
           ?? "unknown";
}

public sealed record SubscriptionDto(
    Guid Id,
    string SystemKey,
    string EventType,
    string PayloadFormatKey,
    bool Enabled,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc)
{
    public static SubscriptionDto FromEntity(SystemEventSubscription e) =>
        new(e.Id, e.SystemKey, e.EventType, e.PayloadFormatKey, e.Enabled, e.CreatedAtUtc, e.UpdatedAtUtc);
}

public sealed record CreateSubscriptionRequest(
    string EventType,
    string PayloadFormatKey,
    bool? Enabled);

public sealed record PatchSubscriptionRequest(
    bool? Enabled,
    string? PayloadFormatKey);
