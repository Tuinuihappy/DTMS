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
                        UpdatedAt: cred.UpdatedAt,
                        CallbackTokenExpiresAt: TryReadJwtExpiry(cred.CallbackAuthConfig))));
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
                // token (server JSON-encodes it), null to clear outbound
                // auth entirely, OR blank-while-scheme-unchanged to keep
                // the existing token — the frontend's "Leave blank to
                // keep current value" hint relies on the third path so
                // operators can rotate URL/timeout without re-pasting a
                // 600-char JWT every time.
                string? authConfigJson = null;
                if (req.CallbackAuthScheme is { Length: > 0 } scheme)
                {
                    if (scheme.ToLowerInvariant() != "bearer")
                        return Results.BadRequest(new { error = "Only 'bearer' callback auth scheme supported in MVP." });
                    if (string.IsNullOrWhiteSpace(req.CallbackBearerToken))
                    {
                        // Preserve the existing token when the request
                        // leaves the field blank — but only if there IS
                        // a token to preserve. Otherwise (first-time
                        // configure with no token) reject with a clear
                        // message rather than silently saving an empty
                        // bearer config that would 401 on every call.
                        if (string.IsNullOrWhiteSpace(cred.CallbackAuthConfig))
                            return Results.BadRequest(new
                            {
                                error = "callbackBearerToken required when setting bearer scheme for the first time — no existing token to preserve.",
                            });
                        authConfigJson = cred.CallbackAuthConfig;
                    }
                    else
                    {
                        authConfigJson = JsonSerializer.Serialize(new { token = req.CallbackBearerToken });
                    }
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

        // ── Hard delete (only when inactive) ─────────────────────────
        // Soft delete via /deactivate is the routine path. Hard delete
        // is the rare cleanup operation (test data, GDPR purge, retired
        // system that will never be onboarded again under the same key).
        // We gate on IsActive=false to force operators to stop traffic
        // first — admin clicking DELETE in production by mistake would
        // otherwise blow the system off the auth surface mid-request.
        //
        // Cascade in the Iam schema removes SystemCredentials,
        // SystemClientPermissions, and SystemEventSubscriptions; cross-
        // module historical data (DeliveryOrders.SourceSystem, outbox
        // PartitionKey, audit log entries) is preserved as denormalized
        // string references with no FK, so deleting the system row does
        // not break audit timelines or order history.
        group.MapDelete("/{key}",
            async (string key, HttpContext ctx,
                   ISystemClientRepository systems,
                   IAuditLogRepository audit,
                   CancellationToken ct) =>
            {
                var client = await systems.GetByKeyAsync(key, ct);
                if (client is null) return Results.NotFound();
                if (client.IsActive)
                    return Results.Conflict(new
                    {
                        error = "Deactivate the system first. Hard delete is only allowed once the row is Inactive and no traffic is flowing.",
                    });

                await systems.RemoveAsync(client, ct);
                await audit.AppendAsync(new PermissionAuditEntry(
                    actorEmployeeId: ActorOrUnknown(ctx),
                    action: "system-hard-deleted",
                    permissionCode: null,
                    details: JsonSerializer.Serialize(new
                    {
                        systemKey = key,
                        displayName = client.DisplayName,
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

    /// <summary>
    /// Phase S.6 follow-up — best-effort JWT expiry extraction. Decodes
    /// the payload (segment 2) of a Bearer-style token stored under
    /// <c>{"token":"<jwt>"}</c> in <c>SystemCredentials.CallbackAuthConfig</c>
    /// and returns the <c>exp</c> claim as UTC. Used to render
    /// "expires in N days" warnings on the system detail page.
    ///
    /// Returns null for any of: empty config, malformed JSON, non-JWT
    /// token, missing <c>exp</c> claim. We do NOT verify the signature
    /// — the OMS issuer holds that key, and a tampered token would just
    /// fail at OMS-side validation. This decode is for UX surfacing only.
    /// </summary>
    private static DateTime? TryReadJwtExpiry(string? callbackAuthConfig)
    {
        if (string.IsNullOrWhiteSpace(callbackAuthConfig)) return null;
        try
        {
            using var configDoc = System.Text.Json.JsonDocument.Parse(callbackAuthConfig);
            if (!configDoc.RootElement.TryGetProperty("token", out var tokenEl)) return null;
            if (tokenEl.ValueKind != System.Text.Json.JsonValueKind.String) return null;
            var jwt = tokenEl.GetString();
            if (string.IsNullOrWhiteSpace(jwt)) return null;

            var parts = jwt.Split('.');
            if (parts.Length < 2) return null;

            // base64url → base64 (RFC 7515): replace -/_ with +/, re-pad
            var payloadB64 = parts[1].Replace('-', '+').Replace('_', '/');
            switch (payloadB64.Length % 4) { case 2: payloadB64 += "=="; break; case 3: payloadB64 += "="; break; }
            var payloadBytes = Convert.FromBase64String(payloadB64);

            using var payloadDoc = System.Text.Json.JsonDocument.Parse(payloadBytes);
            if (!payloadDoc.RootElement.TryGetProperty("exp", out var expEl)) return null;
            if (expEl.ValueKind != System.Text.Json.JsonValueKind.Number) return null;
            var expSeconds = expEl.GetInt64();
            return DateTimeOffset.FromUnixTimeSeconds(expSeconds).UtcDateTime;
        }
        catch
        {
            // Any decode/parse failure → silently fall through to null.
            // The UI shows "no expiry info" rather than an error toast —
            // this is auxiliary information, not a hard requirement.
            return null;
        }
    }
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
    DateTime UpdatedAt,
    // Phase S.6 follow-up — when CallbackAuthScheme=bearer and the stored
    // token is a JWT with an `exp` claim, decode it and surface the expiry
    // so the UI can warn operators before OMS auth starts failing. Null
    // when no callback configured, not a JWT, or no exp claim.
    DateTime? CallbackTokenExpiresAt);

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
