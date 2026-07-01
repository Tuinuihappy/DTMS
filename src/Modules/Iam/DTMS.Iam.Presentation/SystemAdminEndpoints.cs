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
/// <para><b>Scope.</b> Identity + credential + callback config CRUD,
/// activate / deactivate, hard delete (gated on IsActive=false; see
/// the DELETE handler), inbound credential rotate, and per-system
/// permission grant / revoke (Phase S.7 — mirrors role-side
/// /roles/{name}/permissions/{code}). Subscription CRUD lives in
/// <see cref="SystemSubscriptionEndpoints"/>.</para>
/// </summary>
public static class SystemAdminEndpoints
{
    public static void MapSystemAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/iam/systems")
            .WithTags("IamSystemAdmin")
            .RequireAuthorization();

        // ── Create: provisions client + auto-seeds standard perms + ─────
        //     mints inbound credential (plaintext returned ONE TIME).
        //     Default scheme = api-key; pass authScheme="bearer-jwt" to
        //     mint an OAuth client_secret instead — partner then POSTs to
        //     /oauth/token to exchange it for a short-lived JWT.
        group.MapPost("/",
            async (CreateSystemRequest req, HttpContext ctx,
                   ISystemClientRepository systems,
                   ISystemCredentialRepository creds,
                   IAuditLogRepository audit,
                   CancellationToken ct) =>
            {
                if (await systems.GetByKeyAsync(req.Key, ct) is not null)
                    return Results.Conflict(new { error = $"System '{req.Key}' already exists." });

                var scheme = NormaliseScheme(req.AuthScheme);
                if (scheme is null)
                    return Results.BadRequest(new
                    {
                        error = "AuthScheme must be 'bearer-jwt' (or omitted to default).",
                    });

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

                    var minted = MintCredential(req.Key, scheme);
                    var credential = new SystemCredential(
                        systemKey: req.Key,
                        authScheme: scheme,
                        authConfig: minted.AuthConfigJson);
                    await creds.AddAsync(credential, ct);

                    await audit.AppendAsync(new PermissionAuditEntry(
                        actorEmployeeId: ActorOrUnknown(ctx),
                        action: "system-created",
                        permissionCode: null,
                        details: JsonSerializer.Serialize(new
                        {
                            systemKey = req.Key,
                            authScheme = scheme,
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
                            AuthScheme: scheme,
                            Secret: minted.Plaintext));
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

        // ── Rotate inbound credential (scheme-preserving) ─────────────
        //     Mints a new secret of WHATEVER scheme the credential is
        //     currently on. To switch schemes use PUT /credential/scheme
        //     below.
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

                if (cred.AuthScheme != "bearer-jwt")
                    return Results.Conflict(new
                    {
                        error = $"Unsupported AuthScheme '{cred.AuthScheme}' on row — only bearer-jwt is supported. Fix the DB row or PUT /credential/scheme.",
                    });

                var minted = MintCredential(key, cred.AuthScheme);
                cred.RotateInbound(
                    authScheme: cred.AuthScheme,
                    authConfig: minted.AuthConfigJson);
                await creds.UpdateAsync(cred, ct);

                // Evict the cached credential so the next inbound call
                // re-reads from DB. Without this the old secret would
                // continue to authenticate up to the 5-minute L2 TTL.
                await reader.InvalidateAsync(key, ct);

                await audit.AppendAsync(new PermissionAuditEntry(
                    actorEmployeeId: ActorOrUnknown(ctx),
                    action: "system-credential-rotated",
                    permissionCode: null,
                    details: JsonSerializer.Serialize(new
                    {
                        systemKey = key,
                        authScheme = cred.AuthScheme,
                    })), ct);

                return Results.Ok(new RotateCredentialResponse(
                    Secret: minted.Plaintext,
                    AuthScheme: cred.AuthScheme,
                    RotatedAt: cred.UpdatedAt));
            }).RequirePermission("dtms:iam:system:write");

        // ── Switch inbound auth scheme (api-key ↔ bearer-jwt) ─────────
        //     Replaces the credential with a fresh secret of the new
        //     scheme. Cache invalidated so the change takes effect within
        //     one Redis round-trip across all pods. Partner must update
        //     their integration to match the new scheme before the next
        //     call — there's no grace period because one credential row
        //     holds one scheme at a time.
        group.MapPut("/{key}/credential/scheme",
            async (string key, RotateSchemeRequest req, HttpContext ctx,
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

                var newScheme = NormaliseScheme(req.AuthScheme);
                if (newScheme is null)
                    return Results.BadRequest(new
                    {
                        error = "AuthScheme must be 'bearer-jwt'.",
                    });

                var fromScheme = cred.AuthScheme;
                var minted = MintCredential(key, newScheme);
                cred.RotateInbound(
                    authScheme: newScheme,
                    authConfig: minted.AuthConfigJson);
                await creds.UpdateAsync(cred, ct);
                await reader.InvalidateAsync(key, ct);

                await audit.AppendAsync(new PermissionAuditEntry(
                    actorEmployeeId: ActorOrUnknown(ctx),
                    action: "system-credential-scheme-changed",
                    permissionCode: null,
                    details: JsonSerializer.Serialize(new
                    {
                        systemKey = key,
                        fromScheme,
                        toScheme = newScheme,
                    })), ct);

                return Results.Ok(new RotateCredentialResponse(
                    Secret: minted.Plaintext,
                    AuthScheme: newScheme,
                    RotatedAt: cred.UpdatedAt));
            }).RequirePermission("dtms:iam:system:write");

        // ── Admin-issued long-lived JWT (escape hatch for partners that ─
        //     can't run an OAuth client). Bypasses /oauth/token: admin
        //     mints a JWT here with a custom lifetime (default 90 days,
        //     max 365 days) and hands it to the partner via a secure
        //     channel. Partner sends it as `Authorization: Bearer ...`
        //     directly — no token refresh logic on their side.
        //
        //     Phase S.8c — the mint is recorded in iam.SystemIssuedTokens
        //     so admins can list + revoke individual tokens without
        //     having to nuke the entire system.
        group.MapPost("/{key}/issue-token",
            async (string key, IssueTokenRequest req, HttpContext ctx,
                   ISystemClientRepository systems,
                   ISystemCredentialRepository creds,
                   ISystemIssuedTokenRepository issuedTokens,
                   ISystemJwtIssuer issuer,
                   IAuditLogRepository audit,
                   CancellationToken ct) =>
            {
                var client = await systems.GetByKeyAsync(key, ct);
                if (client is null) return Results.NotFound();
                if (!client.IsActive)
                    return Results.Conflict(new { error = $"System '{key}' is deactivated — reactivate first." });

                var cred = await creds.GetBySystemKeyAsync(key, ct);
                if (cred is null || cred.AuthScheme != "bearer-jwt")
                    return Results.Conflict(new
                    {
                        error = $"System '{key}' is not configured for bearer-jwt. Switch scheme via PUT /credential/scheme first.",
                    });

                // 90 days default — long enough that partners with a quarterly
                // change-window can cope, short enough that a forgotten-in-
                // pastebin token stops working before the next audit.
                var lifetime = req.LifetimeSeconds ?? 7_776_000;
                if (lifetime < 60)
                    return Results.BadRequest(new { error = "LifetimeSeconds must be at least 60." });
                // Hard cap at 365 days — anything longer is essentially a
                // forever-token, at which point use a different mechanism
                // (e.g. mTLS) rather than pretending it's a JWT.
                if (lifetime > 31_536_000)
                    return Results.BadRequest(new { error = "LifetimeSeconds must be at most 31,536,000 (365 days)." });

                var token = issuer.Issue(key, lifetimeSecondsOverride: lifetime);

                // Phase S.8c — persist for the admin list + revoke UI.
                // Only stores metadata (jti, exp, issuer identity) — never
                // the JWT body itself.
                await issuedTokens.AddAsync(new SystemIssuedToken(
                    id: Guid.NewGuid(),
                    systemKey: key,
                    jti: token.Jti,
                    issuedAt: DateTime.UtcNow,
                    expiresAt: token.ExpiresAt,
                    issuedBy: ActorOrUnknown(ctx)), ct);

                await audit.AppendAsync(new PermissionAuditEntry(
                    actorEmployeeId: ActorOrUnknown(ctx),
                    action: "system-token-issued",
                    permissionCode: null,
                    details: JsonSerializer.Serialize(new
                    {
                        systemKey = key,
                        jti = token.Jti,
                        lifetimeSeconds = lifetime,
                        expiresAt = token.ExpiresAt,
                    })), ct);

                return Results.Ok(new IssueTokenResponse(
                    AccessToken: token.AccessToken,
                    TokenType: "Bearer",
                    ExpiresInSeconds: token.ExpiresInSeconds,
                    ExpiresAt: token.ExpiresAt,
                    Jti: token.Jti));
            }).RequirePermission("dtms:iam:system:write");

        // ── Phase S.8c — list admin-issued tokens for a system ────────
        //     Newest-first. Includes Active + Revoked (audit trail).
        //     Response omits sensitive fields — jti is the only opaque
        //     identifier the UI needs to feed into the revoke endpoint.
        group.MapGet("/{key}/tokens",
            async (string key,
                   ISystemClientRepository systems,
                   ISystemIssuedTokenRepository issuedTokens,
                   CancellationToken ct) =>
            {
                if (await systems.GetByKeyAsync(key, ct) is null)
                    return Results.NotFound();

                var rows = await issuedTokens.ListBySystemAsync(key, ct);
                return Results.Ok(rows.Select(t => new IssuedTokenSummary(
                    Jti: t.Jti,
                    IssuedAt: t.IssuedAt,
                    ExpiresAt: t.ExpiresAt,
                    IssuedBy: t.IssuedBy,
                    Status: t.Status.ToString(),
                    RevokedAt: t.RevokedAt,
                    RevokedBy: t.RevokedBy,
                    RevokeReason: t.RevokeReason)));
            }).RequirePermission("dtms:iam:system:read");

        // ── Phase S.8c — revoke an admin-issued token by jti ──────────
        //     Idempotent: re-revoking is a no-op that returns 204. Redis
        //     TTL matches the token's remaining lifetime, so the block-
        //     list entry drops on its own after natural expiry — no
        //     cleanup job needed. The DB row lives forever (audit).
        group.MapPost("/{key}/tokens/{jti}/revoke",
            async (string key, string jti, RevokeTokenRequest? req, HttpContext ctx,
                   ISystemClientRepository systems,
                   ISystemIssuedTokenRepository issuedTokens,
                   ISystemJwtRevocationList revocationList,
                   IAuditLogRepository audit,
                   CancellationToken ct) =>
            {
                if (await systems.GetByKeyAsync(key, ct) is null)
                    return Results.NotFound(new { error = $"System '{key}' not found." });

                var row = await issuedTokens.GetByJtiAsync(jti, ct);
                if (row is null)
                    return Results.NotFound(new { error = $"Token with jti '{jti}' not found." });
                // Cross-check: jti must belong to the system on the URL.
                // Prevents admin-of-system-A from revoking system-B's
                // tokens via URL manipulation (the audit trail would be
                // filed under system-A which is misleading).
                if (!string.Equals(row.SystemKey, key, StringComparison.Ordinal))
                    return Results.NotFound(new { error = $"Token with jti '{jti}' not found for system '{key}'." });

                // Idempotent: already revoked → 204 + skip Redis write.
                // Prevents duplicate audit rows when the UI double-clicks
                // or a retry lands on an already-revoked token.
                if (row.Status == SystemIssuedTokenStatus.Revoked)
                    return Results.NoContent();

                // Redis first — if the blocklist write fails, throw and
                // let the caller retry. Alternative (write DB first, then
                // Redis) would leave a "logically revoked but effectively
                // active" state on Redis failure; caller retries would
                // then see "already revoked" and skip Redis forever.
                await revocationList.RevokeAsync(row.Jti, row.ExpiresAt, ct);

                row.Revoke(ActorOrUnknown(ctx), req?.Reason);
                await issuedTokens.UpdateAsync(row, ct);

                await audit.AppendAsync(new PermissionAuditEntry(
                    actorEmployeeId: ActorOrUnknown(ctx),
                    action: "system-token-revoked",
                    permissionCode: null,
                    details: JsonSerializer.Serialize(new
                    {
                        systemKey = key,
                        jti = row.Jti,
                        reason = req?.Reason,
                    })), ct);

                return Results.NoContent();
            }).RequirePermission("dtms:iam:system:write");

        // ── Grant permission to system ──────────────────────────────────
        //     Mirrors role-side POST /roles/{name}/permissions/{code}.
        //     Catalog existence check intentionally skipped — system perms
        //     can be runtime-resolved templates (dtms:source:{key}:order:*)
        //     that don't sit in iam.permissions. Endpoint is locked behind
        //     dtms:iam:system:write so bad-data risk is bounded.
        group.MapPost("/{key}/permissions/{code}",
            async (string key, string code, HttpContext ctx,
                   ISystemClientRepository systems,
                   IAuditLogRepository audit,
                   Microsoft.Extensions.Caching.Memory.IMemoryCache cache,
                   CancellationToken ct) =>
            {
                if (await systems.GetByKeyAsync(key, ct) is null)
                    return Results.NotFound(new { error = $"System '{key}' not found." });

                if (string.IsNullOrWhiteSpace(code) || code.Length > 120)
                    return Results.BadRequest(new { error = "PermissionCode must be 1-120 chars." });

                var inserted = await systems.GrantPermissionAsync(key, code, ActorOrUnknown(ctx), ct);
                if (inserted)
                {
                    // Phase S.8b — evict the PermissionClaimsTransformer
                    // cache so system JWTs already in flight pick up the
                    // grant on their next request (else it takes 5 min).
                    cache.Remove($"iam:sys-perms:{key}");

                    await audit.AppendAsync(new PermissionAuditEntry(
                        actorEmployeeId: ActorOrUnknown(ctx),
                        action: "system-grant",
                        permissionCode: code,
                        details: JsonSerializer.Serialize(new { systemKey = key })), ct);
                }
                return Results.NoContent();
            }).RequirePermission("dtms:iam:system:write");

        // ── Revoke permission from system ───────────────────────────────
        //     Symmetric with grant. No self-lockout guard needed (admin
        //     identity is user-side, not system-side). Standard auto-seed
        //     perms can also be revoked — the proper kill switch for a
        //     misbehaving integration is Deactivate, not perm stripping.
        group.MapDelete("/{key}/permissions/{code}",
            async (string key, string code, HttpContext ctx,
                   ISystemClientRepository systems,
                   IAuditLogRepository audit,
                   Microsoft.Extensions.Caching.Memory.IMemoryCache cache,
                   CancellationToken ct) =>
            {
                if (await systems.GetByKeyAsync(key, ct) is null)
                    return Results.NotFound(new { error = $"System '{key}' not found." });

                var deleted = await systems.RevokePermissionAsync(key, code, ct);
                if (deleted)
                {
                    // Phase S.8b — evict transformer cache so revokes take
                    // effect immediately for tokens already in flight
                    // (otherwise the granted claim persists in principals
                    // reconstructed from cache for up to 5 minutes).
                    cache.Remove($"iam:sys-perms:{key}");

                    await audit.AppendAsync(new PermissionAuditEntry(
                        actorEmployeeId: ActorOrUnknown(ctx),
                        action: "system-revoke",
                        permissionCode: code,
                        details: JsonSerializer.Serialize(new { systemKey = key })), ct);
                }
                return Results.NoContent();
            }).RequirePermission("dtms:iam:system:write");
    }

    private static string ActorOrUnknown(HttpContext ctx)
        => ctx.User.FindFirst("sub")?.Value
           ?? ctx.User.Identity?.Name
           ?? "unknown";

    /// <summary>
    /// Coerce an inbound auth scheme name to the canonical lowercase form
    /// the middleware switch checks. Single scheme today —
    /// <c>bearer-jwt</c> — but kept as a function so adding a second
    /// scheme later is a one-liner rather than touching every endpoint.
    /// Null/empty defaults to <c>bearer-jwt</c>. Returns null for any
    /// other value so the caller returns 400.
    /// </summary>
    private static string? NormaliseScheme(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "bearer-jwt";
        var v = value.Trim().ToLowerInvariant();
        return v is "bearer-jwt" ? v : null;
    }

    /// <summary>
    /// Mint the client_secret + AuthConfig JSON for a bearer-jwt system.
    /// Plaintext is returned to the caller exactly ONCE — only the
    /// SHA-256 hash lives in <see cref="SystemCredential.AuthConfig"/>.
    /// </summary>
    private static MintedCredential MintCredential(string systemKey, string scheme)
    {
        // Defensive — NormaliseScheme above is the only producer of
        // `scheme` and only emits "bearer-jwt", but assert anyway so a
        // future second scheme can't silently fall through to wrong shape.
        if (scheme != "bearer-jwt")
            throw new InvalidOperationException(
                $"MintCredential called with unsupported scheme '{scheme}'. " +
                "Update NormaliseScheme + this switch in lockstep when adding schemes.");

        var s = ClientSecretGenerator.Mint(systemKey);
        // tokenLifetimeSeconds=0 means "use the issuer's default"
        // (configured globally via Jwt:SystemTokenLifetimeSeconds).
        // Per-credential override path stays open via direct DB edit
        // until a UI for it lands.
        return new MintedCredential(
            Plaintext: s.Plaintext,
            AuthConfigJson: JsonSerializer.Serialize(new
            {
                clientSecretHash = s.Sha256Hex,
                tokenLifetimeSeconds = 0,
            }));
    }

    private sealed record MintedCredential(string Plaintext, string AuthConfigJson);

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
    bool? IsActive,
    // Single supported value today: "bearer-jwt" (OAuth client_credentials
    // grant via /oauth/token). Field kept for forward compat — add hmac /
    // partner-signed JWT here in the future. Omit to default to bearer-jwt.
    string? AuthScheme = null);

public sealed record RotateSchemeRequest(string AuthScheme);

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
    // AuthScheme="bearer-jwt" → Secret is "dtms_cs_<key>_..." (client_secret).
    // Returned plaintext exactly once; only the SHA-256 hash is stored.
    string AuthScheme,
    string Secret);

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

public sealed record RotateCredentialResponse(
    // See CreatedSystemResponse for the Secret/AuthScheme contract.
    string Secret,
    string AuthScheme,
    DateTime RotatedAt);

public sealed record IssueTokenRequest(
    // Optional override. Default is 90 days when omitted. Bounded by
    // [60s, 365d] at the endpoint — outside that range the request
    // returns 400. The endpoint never silently clamps; admin sees what
    // they get.
    int? LifetimeSeconds = null);

public sealed record IssueTokenResponse(
    string AccessToken,
    string TokenType,
    int ExpiresInSeconds,
    DateTime ExpiresAt,
    // Phase S.8c — surfaced so the admin UI can persist the mapping to
    // the row it lists, and callers with scripting workflows can revoke
    // without a separate lookup.
    string Jti);

public sealed record RevokeTokenRequest(string? Reason = null);

public sealed record IssuedTokenSummary(
    string Jti,
    DateTime IssuedAt,
    DateTime ExpiresAt,
    string IssuedBy,
    string Status,
    DateTime? RevokedAt,
    string? RevokedBy,
    string? RevokeReason);
