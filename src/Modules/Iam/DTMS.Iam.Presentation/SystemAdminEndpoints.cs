using System.Text.Json;
using DTMS.Iam.Application.Authorization;
using DTMS.Iam.Application.Repositories;
using DTMS.Iam.Domain.Entities;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DTMS.Iam.Presentation;

/// <summary>
/// Phase S.4 — admin surface for the federated source-system world.
/// Replaces the manual <c>psql</c> onboarding ritual (insert
/// SystemClient + SystemCredential + perm rows + compute SHA256 by
/// hand) with an HTTP API.
///
/// <para><b>Scope kept narrow on purpose.</b> No DELETE (deactivate
/// instead — cascade impact across credentials, perms, subscriptions,
/// outbox rows is too large for a routine endpoint; route real
/// deletions through SQL ops). No fine-grained permission grant /
/// revoke — the S.3.1a auto-seed of standard permissions covers the
/// common case; a future endpoint can split if usage demands it.</para>
/// </summary>
public static class SystemAdminEndpoints
{
    public static void MapSystemAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/iam/systems")
            .WithTags("IamSystemAdmin")
            .RequireAuthorization();

        // ── Create: provisions client + auto-seeds standard perms + ─────
        //     mints inbound API key (plaintext returned ONE TIME).
        group.MapPost("/",
            async (CreateSystemRequest req, HttpContext ctx,
                   ISystemClientRepository systems,
                   ISystemCredentialRepository creds,
                   IAuditLogRepository audit,
                   CancellationToken ct) =>
            {
                if (await systems.GetByKeyAsync(req.Key, ct) is not null)
                    return Results.Conflict(new { error = $"System '{req.Key}' already exists." });

                try
                {
                    var client = new SystemClient(
                        key: req.Key,
                        displayName: req.DisplayName,
                        description: req.Description,
                        ownerContact: req.OwnerContact,
                        isActive: req.IsActive ?? true);

                    var permissions = StandardSystemPermissions.All
                        .Select(t => StandardSystemPermissions.Resolve(t, req.Key))
                        .ToList();

                    await systems.AddWithPermissionsAsync(
                        client, permissions, grantedBy: ActorOrUnknown(ctx), ct);

                    var apiKey = ApiKeyGenerator.Mint(req.Key);
                    var credential = new SystemCredential(
                        systemKey: req.Key,
                        authScheme: "api-key",
                        authConfig: JsonSerializer.Serialize(new { keyHash = apiKey.Sha256Hex }));
                    await creds.AddAsync(credential, ct);

                    await audit.AppendAsync(new PermissionAuditEntry(
                        actorEmployeeId: ActorOrUnknown(ctx),
                        action: "system-created",
                        permissionCode: null,
                        details: JsonSerializer.Serialize(new
                        {
                            systemKey = req.Key,
                            permissions,
                        })), ct);

                    return Results.Created(
                        $"/api/v1/iam/systems/{client.Key}",
                        new CreatedSystemResponse(
                            Key: client.Key,
                            DisplayName: client.DisplayName,
                            Description: client.Description,
                            IsActive: client.IsActive,
                            OwnerContact: client.OwnerContact,
                            CreatedAt: client.CreatedAt,
                            Permissions: permissions,
                            ApiKey: apiKey.Plaintext));
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
            }).RequirePermission("dtms:iam:system:write");

        // ── List ──────────────────────────────────────────────────────
        group.MapGet("/",
            async (ISystemClientRepository systems, CancellationToken ct) =>
            {
                var rows = await systems.ListAllAsync(ct);
                return Results.Ok(rows.Select(SystemSummaryDto.FromEntity));
            }).RequirePermission("dtms:iam:system:read");

        // ── Detail (system + credential metadata, no secret hash) ─────
        group.MapGet("/{key}",
            async (string key,
                   ISystemClientRepository systems,
                   ISystemCredentialRepository creds,
                   ISystemEventSubscriptionRepository subs,
                   CancellationToken ct) =>
            {
                var client = await systems.GetByKeyAsync(key, ct);
                if (client is null) return Results.NotFound();

                var cred = await creds.GetBySystemKeyAsync(key, ct);
                var permissions = await systems.GetPermissionCodesAsync(key, ct);
                var subscriptions = await subs.ListBySystemAsync(key, ct);

                return Results.Ok(new SystemDetailDto(
                    Key: client.Key,
                    DisplayName: client.DisplayName,
                    Description: client.Description,
                    IsActive: client.IsActive,
                    OwnerContact: client.OwnerContact,
                    CreatedAt: client.CreatedAt,
                    Permissions: permissions,
                    Subscriptions: subscriptions
                        .Select(s => new SubscriptionSummary(s.EventType, s.PayloadFormatKey, s.Enabled))
                        .ToList(),
                    Credential: cred is null ? null : new CredentialSummary(
                        AuthScheme: cred.AuthScheme,
                        HasCallbackBaseUrl: !string.IsNullOrWhiteSpace(cred.CallbackBaseUrl),
                        CallbackBaseUrl: cred.CallbackBaseUrl,
                        CallbackAuthScheme: cred.CallbackAuthScheme,
                        CallbackTimeoutMs: cred.CallbackTimeoutMs,
                        UpdatedAt: cred.UpdatedAt)));
            }).RequirePermission("dtms:iam:system:read");

        // ── Patch metadata ────────────────────────────────────────────
        group.MapPatch("/{key}",
            async (string key, PatchSystemRequest req, HttpContext ctx,
                   ISystemClientRepository systems,
                   IAuditLogRepository audit,
                   CancellationToken ct) =>
            {
                var client = await systems.GetByKeyAsync(key, ct);
                if (client is null) return Results.NotFound();

                try
                {
                    if (req.DisplayName is { Length: > 0 } dn) client.UpdateDisplayName(dn);
                    if (req.Description is not null) client.UpdateDescription(req.Description);
                    if (req.OwnerContact is not null) client.UpdateOwnerContact(req.OwnerContact);
                    await systems.UpdateAsync(client, ct);

                    await audit.AppendAsync(new PermissionAuditEntry(
                        actorEmployeeId: ActorOrUnknown(ctx),
                        action: "system-updated",
                        permissionCode: null,
                        details: JsonSerializer.Serialize(new { systemKey = key, req })), ct);

                    return Results.Ok(SystemSummaryDto.FromEntity(client));
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
            }).RequirePermission("dtms:iam:system:write");

        // ── Activate / Deactivate ─────────────────────────────────────
        group.MapPost("/{key}/activate",
            async (string key, HttpContext ctx,
                   ISystemClientRepository systems,
                   IAuditLogRepository audit,
                   CancellationToken ct) =>
            {
                var client = await systems.GetByKeyAsync(key, ct);
                if (client is null) return Results.NotFound();
                if (client.IsActive) return Results.NoContent();

                client.Activate();
                await systems.UpdateAsync(client, ct);
                await audit.AppendAsync(new PermissionAuditEntry(
                    actorEmployeeId: ActorOrUnknown(ctx),
                    action: "system-activated",
                    permissionCode: null,
                    details: $"{{\"systemKey\":\"{key}\"}}"), ct);
                return Results.NoContent();
            }).RequirePermission("dtms:iam:system:write");

        group.MapPost("/{key}/deactivate",
            async (string key, HttpContext ctx,
                   ISystemClientRepository systems,
                   IAuditLogRepository audit,
                   CancellationToken ct) =>
            {
                var client = await systems.GetByKeyAsync(key, ct);
                if (client is null) return Results.NotFound();
                if (!client.IsActive) return Results.NoContent();

                client.Deactivate();
                await systems.UpdateAsync(client, ct);
                await audit.AppendAsync(new PermissionAuditEntry(
                    actorEmployeeId: ActorOrUnknown(ctx),
                    action: "system-deactivated",
                    permissionCode: null,
                    details: $"{{\"systemKey\":\"{key}\"}}"), ct);
                return Results.NoContent();
            }).RequirePermission("dtms:iam:system:write");

        // ── Set callback config (URL + outbound auth + timeouts) ──────
        group.MapPut("/{key}/callback",
            async (string key, CallbackConfigRequest req, HttpContext ctx,
                   ISystemClientRepository systems,
                   ISystemCredentialRepository creds,
                   IAuditLogRepository audit,
                   CancellationToken ct) =>
            {
                if (await systems.GetByKeyAsync(key, ct) is null) return Results.NotFound();

                var cred = await creds.GetBySystemKeyAsync(key, ct);
                if (cred is null)
                    return Results.Conflict(new { error = $"No credential row for '{key}'. POST to /api/v1/iam/systems first." });

                // For the bearer scheme we accept either a plaintext
                // token (server JSON-encodes it) or null to clear
                // outbound auth entirely.
                string? authConfigJson = null;
                if (req.CallbackAuthScheme is { Length: > 0 } scheme)
                {
                    if (scheme.ToLowerInvariant() != "bearer")
                        return Results.BadRequest(new { error = "Only 'bearer' callback auth scheme supported in MVP." });
                    if (string.IsNullOrWhiteSpace(req.CallbackBearerToken))
                        return Results.BadRequest(new { error = "callbackBearerToken required when callbackAuthScheme is set." });
                    authConfigJson = JsonSerializer.Serialize(new { token = req.CallbackBearerToken });
                }

                cred.SetCallback(
                    baseUrl: req.CallbackBaseUrl,
                    authScheme: req.CallbackAuthScheme,
                    authConfig: authConfigJson,
                    timeoutMs: req.CallbackTimeoutMs);
                if (req.RetryMaxAttempts is int rma
                    || req.CircuitFailureThreshold is int cft
                    || req.CircuitDurationSeconds is int cds)
                {
                    cred.UpdateResilience(
                        retryMaxAttempts: req.RetryMaxAttempts ?? cred.RetryMaxAttempts,
                        circuitFailureThreshold: req.CircuitFailureThreshold ?? cred.CircuitFailureThreshold,
                        circuitDurationSeconds: req.CircuitDurationSeconds ?? cred.CircuitDurationSeconds);
                }
                await creds.UpdateAsync(cred, ct);

                await audit.AppendAsync(new PermissionAuditEntry(
                    actorEmployeeId: ActorOrUnknown(ctx),
                    action: "system-callback-updated",
                    permissionCode: null,
                    details: JsonSerializer.Serialize(new
                    {
                        systemKey = key,
                        callbackBaseUrl = req.CallbackBaseUrl,
                        callbackAuthScheme = req.CallbackAuthScheme,
                        // Never log token plaintext.
                        tokenSet = !string.IsNullOrWhiteSpace(req.CallbackBearerToken),
                    })), ct);

                return Results.NoContent();
            }).RequirePermission("dtms:iam:system:write");

        // ── Rotate inbound api-key ────────────────────────────────────
        group.MapPost("/{key}/credential/rotate",
            async (string key, HttpContext ctx,
                   ISystemClientRepository systems,
                   ISystemCredentialRepository creds,
                   CachedCredentialReader reader,
                   IAuditLogRepository audit,
                   CancellationToken ct) =>
            {
                if (await systems.GetByKeyAsync(key, ct) is null) return Results.NotFound();
                var cred = await creds.GetBySystemKeyAsync(key, ct);
                if (cred is null)
                    return Results.Conflict(new { error = $"No credential row for '{key}'." });

                var apiKey = ApiKeyGenerator.Mint(key);
                cred.RotateInbound(
                    authScheme: "api-key",
                    authConfig: JsonSerializer.Serialize(new { keyHash = apiKey.Sha256Hex }));
                await creds.UpdateAsync(cred, ct);

                // Evict the cached credential so the next inbound call
                // re-reads from DB. Without this the old key would
                // continue to authenticate up to the 5-minute L2 TTL.
                await reader.InvalidateAsync(key, ct);

                await audit.AppendAsync(new PermissionAuditEntry(
                    actorEmployeeId: ActorOrUnknown(ctx),
                    action: "system-credential-rotated",
                    permissionCode: null,
                    details: $"{{\"systemKey\":\"{key}\"}}"), ct);

                return Results.Ok(new RotateCredentialResponse(apiKey.Plaintext, cred.UpdatedAt));
            }).RequirePermission("dtms:iam:system:write");
    }

    private static string ActorOrUnknown(HttpContext ctx)
        => ctx.User.FindFirst("sub")?.Value
           ?? ctx.User.Identity?.Name
           ?? "unknown";
}

// ── DTOs ───────────────────────────────────────────────────────────────

public sealed record CreateSystemRequest(
    string Key,
    string DisplayName,
    string? Description,
    string? OwnerContact,
    bool? IsActive);

public sealed record PatchSystemRequest(
    string? DisplayName,
    string? Description,
    string? OwnerContact);

public sealed record CallbackConfigRequest(
    string? CallbackBaseUrl,
    string? CallbackAuthScheme,
    string? CallbackBearerToken,
    int? CallbackTimeoutMs,
    int? RetryMaxAttempts,
    int? CircuitFailureThreshold,
    int? CircuitDurationSeconds);

public sealed record CreatedSystemResponse(
    string Key,
    string DisplayName,
    string? Description,
    bool IsActive,
    string? OwnerContact,
    DateTime CreatedAt,
    IReadOnlyList<string> Permissions,
    string ApiKey);

public sealed record SystemSummaryDto(
    string Key,
    string DisplayName,
    string? Description,
    bool IsActive,
    string? OwnerContact,
    DateTime CreatedAt)
{
    public static SystemSummaryDto FromEntity(SystemClient s) =>
        new(s.Key, s.DisplayName, s.Description, s.IsActive, s.OwnerContact, s.CreatedAt);
}

public sealed record CredentialSummary(
    string AuthScheme,
    bool HasCallbackBaseUrl,
    string? CallbackBaseUrl,
    string? CallbackAuthScheme,
    int CallbackTimeoutMs,
    DateTime UpdatedAt);

public sealed record SubscriptionSummary(string EventType, string PayloadFormatKey, bool Enabled);

public sealed record SystemDetailDto(
    string Key,
    string DisplayName,
    string? Description,
    bool IsActive,
    string? OwnerContact,
    DateTime CreatedAt,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<SubscriptionSummary> Subscriptions,
    CredentialSummary? Credential);

public sealed record RotateCredentialResponse(string ApiKey, DateTime RotatedAt);
