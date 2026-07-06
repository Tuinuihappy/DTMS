# ADR-018: Dual-Issuer Authentication — Scheme Split, Cached Revocation, Distributed Permission Cache

- **Status**: Proposed
- **Date**: 2026-07-06
- **Deciders**: Solo dev (DTMS)
- **Related**: [ADR-014 Mobile API Authentication — External Auth](adr-014-mobile-api-authentication-external-auth.md), [ADR-017 Permission Naming Standard](adr-017-permission-naming-standard.md)

## Context

DTMS authenticates **two distinct classes of caller** whose tokens come from **two different issuers**:

| | Frontend (user) | External system (M2M) |
|---|---|---|
| Issuer | External Auth `10.204.212.28:15000` | DTMS itself (`POST /oauth/token`, client_credentials) |
| Algorithm | RS256 | RS256 (separate keypair) |
| `iss` / `aud` | none (ADR-014 §"Audience separation") | `iss=dtms`, `aud=dtms-api` |
| Revocation | — | Redis `jti` list (`RedisSystemJwtRevocationList`, fail-closed) |

Today **both flow through a single `"Bearer"` JwtBearer scheme** ([`Program.cs:129`](../../../src/DTMS.Api/Program.cs)). The handler is given **both** signing keys via a multi-key `IssuerSigningKeyResolver` ([`Program.cs:154`](../../../src/DTMS.Api/Program.cs)) and `ValidateIssuer/ValidateAudience` are **off**. Since Phase S.8b, a system JWT can hit any `[Authorize]` endpoint — the two trust domains are separated only by permission claims resolved in `PermissionClaimsTransformer`, not at the cryptographic/scheme layer.

A design review of this arrangement surfaced the following defects, most-severe first:

1. **Revocation gap (critical)** — the Redis revocation list is only consulted by `SystemJwtValidator`, which only runs on the `/api/v1/source/*` middleware branch. A system JWT hitting a **generic** `[Authorize]` endpoint is validated by JwtBearer with **signature + expiry only**, and `PermissionClaimsTransformer` ([`PermissionClaimsTransformer.cs:68`](../../../src/Modules/Iam/DTMS.Iam.Application/Authorization/PermissionClaimsTransformer.cs)) does not check revocation. A **revoked** system token therefore keeps working on every generic endpoint until it expires — lifetime is up to **365 days**.
2. **Token / audience confusion** — with `ValidateAudience=false` and the resolver holding both keys, cryptographic separation of the two issuers is not enforced at the scheme layer.
3. **Multi-scheme double-run** — any policy listing two schemes authenticates the token against **both** JwtBearer handlers per request; the wrong one always fails, wasting an RSA verify and emitting a spurious `OnAuthenticationFailed` warning.
4. **Double signature verification** — routing system tokens through both JwtBearer **and** `SystemJwtValidator.Validate(raw)` re-parses and re-verifies the signature a second time on the hot path.
5. **Sync-over-async Redis** — `SystemJwtValidator` blocks on `IsRevokedAsync(jti).GetAwaiter().GetResult()` ([`SystemJwtValidator.cs:134`](../../../src/Modules/Iam/DTMS.Iam.Application/Authorization/SystemJwtValidator.cs)). Acceptable in a sync middleware branch scoped to `/source/*`; promoted to **every** system request it risks thread-pool starvation under M2M load.
6. **Redis on the critical path, fail-closed** — a per-request revocation round-trip makes Redis a hot single point on all system auth; a Redis blip 401s all M2M traffic.
7. **Per-pod permission staleness** — `PermissionClaimsTransformer` uses raw `IMemoryCache` (5-min TTL) and the four admin invalidation sites call `IMemoryCache.Remove` on the **serving pod only** ([`IamEndpoints.cs:212,252`](../../../src/Modules/Iam/DTMS.Iam.Presentation/IamEndpoints.cs), [`SystemAdminEndpoints.cs:628,661`](../../../src/Modules/Iam/DTMS.Iam.Presentation/SystemAdminEndpoints.cs)). On a multi-replica deployment, permission grants/revokes take up to 5 minutes to propagate to other pods, with no cross-pod broadcast.

### Non-problem: per-URL system-key scoping

An earlier draft flagged the loss of route-`{key}` ↔ token-`sub` matching if `SystemClientAuthMiddleware` were retired. **This was moot.** Phase S.8e already removed per-URL system-key matching; identity comes solely from the JWT `sub`, and `SourceSystemPermissionHandler` reads the key from the authenticated principal, not the route. There is no route-key scoping to preserve.

## Decision

Split authentication into **two named schemes behind a `"Bearer"` policy-scheme selector**, add a **cached async revocation** layer, and migrate the permission cache onto the existing **`ITieredCache`** (cross-pod invalidation). Delivered in three phases; Phase 4 collects deferred work with external dependencies.

### Core idea

```
                     ┌ selector reads kid/sub (no verify) ┐
Authorization: Bearer ─► "Bearer" (AddPolicyScheme) ──────┤
                         │                                 │
             kid=dtms-system-* / sub=system:*  ─► SystemJwt┤─► verify ONE key
                         else                   ─► UserJwt ┘   + existing permission model
```

`"Bearer"` changes from *"one handler + two keys"* to *"a selector that forwards to one of two single-key handlers."* The scheme **name** endpoints reference stays `"Bearer"`, so the ~164 `.RequirePermission(...)` call sites and all `.RequireAuthorization()` / `[Authorize]` endpoints require **no edits**.

### Phase 1 — Two schemes under a `"Bearer"` selector

- New `src/DTMS.Api/Auth/AuthSchemes.cs`: `Bearer="Bearer"`, `User="UserJwt"`, `System="SystemJwt"`.
- Rewrite [`Program.cs:129-199`](../../../src/DTMS.Api/Program.cs):

```csharp
builder.Services.AddAuthentication(AuthSchemes.Bearer)
    // Selector: pick the scheme from the token's shape WITHOUT validating it.
    // Misrouting is safe — the chosen handler validates against its single key,
    // so a wrong-key token is rejected, never downgraded.
    .AddPolicyScheme(AuthSchemes.Bearer, "User or System JWT", o =>
    {
        o.ForwardDefaultSelector = ctx =>
        {
            var token = ExtractBearer(ctx);   // Authorization header, or ?access_token= for /hubs
            try
            {
                var jwt = new JsonWebToken(token);
                if (jwt.Kid == jwtSettings.SystemTokenKeyId ||
                    jwt.Subject?.StartsWith("system:", StringComparison.Ordinal) == true)
                    return AuthSchemes.System;
            }
            catch { /* malformed → let UserJwt reject it */ }
            return AuthSchemes.User;
        };
    })
    // USER: External Auth key ONLY. aud stays off until External Auth ships iss/aud (ADR-014).
    .AddJwtBearer(AuthSchemes.User, o =>
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            IssuerSigningKey = externalAuthKey,      // single key, not a two-key resolver
            ValidateIssuer = false,                  // TODO Phase 4: enable once External Auth adds iss
            ValidateAudience = false,                // TODO Phase 4: ValidAudience = "dtms-api"
            ValidateLifetime = true,
            NameClaimType = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name",
            RoleClaimType = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role",
            ClockSkew = TimeSpan.FromSeconds(30),
        };
        o.Events = hubTokenFromQueryEvents;          // OnMessageReceived + OnAuthenticationFailed (unchanged)
    })
    // SYSTEM: DTMS key ONLY + iss/aud. Revocation happens in OnTokenValidated (Phase 2).
    .AddJwtBearer(AuthSchemes.System, o =>
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            IssuerSigningKey = systemJwtKey,
            ValidateIssuer   = true, ValidIssuer   = jwtSettings.SystemTokenIssuer,
            ValidateAudience = true, ValidAudience = jwtSettings.SystemTokenAudience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
        o.Events = systemJwtEvents;                  // Phase 2 fills OnTokenValidated
    });
```

- Delete the two-key `signingKeys[]` resolver ([`Program.cs:125-127,154`](../../../src/DTMS.Api/Program.cs)).
- Keep the `"Bearer"` constant in [`RequirePermissionExtensions.cs:16`](../../../src/Modules/Iam/DTMS.Iam.Application/Authorization/RequirePermissionExtensions.cs) — it now resolves to the selector automatically.

**Closes:** #1 (revocation now reachable on every path via the SystemJwt scheme), #2 (each token verifies against exactly one key), #3 (endpoints pin a single scheme `"Bearer"` that forwards to one concrete handler — no policy lists two schemes, so no double-run).

### Phase 2 — Cached async revocation

- New `CachedSystemJwtRevocationList` wrapping [`ISystemJwtRevocationList`](../../../src/Modules/Iam/DTMS.Iam.Application/Authorization/ISystemJwtRevocationList.cs), using [`ITieredCache`](../../../src/DTMS.SharedKernel/Caching/ITieredCache.cs) to cache the **not-revoked** result with a short TTL (~15–30 s).
- Fill the SystemJwt `OnTokenValidated` as a **genuinely async** event that reads `jti` / `sub` from the already-validated `ctx.Principal` and does revocation + active-client checks only — it must **not** call `SystemJwtValidator.Validate(raw)` again:

```csharp
OnTokenValidated = async ctx =>
{
    var jti = ctx.Principal!.FindFirst("jti")?.Value;
    var sub = ctx.Principal!.FindFirst("sub")?.Value;              // "system:{key}"
    var revoked = ctx.HttpContext.RequestServices.GetRequiredService<CachedSystemJwtRevocationList>();
    var clients = ctx.HttpContext.RequestServices.GetRequiredService<CachedSystemClientReader>();

    if (jti is not null && await revoked.IsRevokedAsync(jti)) { ctx.Fail("token revoked"); return; }

    var key = sub?["system:".Length..];
    var client = key is not null ? await clients.GetAsync(key) : null;
    if (client is null || !client.IsActive) ctx.Fail("unknown or inactive system");
};
```

**Closes:** #4 (signature verified once by the handler; the event does revocation only), #5 (`await` instead of `.GetAwaiter().GetResult()`), #6 (short-TTL negative cache keeps Redis off the hot path and survives brief Redis outages).

### Phase 3 — Distributed permission cache

- Migrate [`PermissionClaimsTransformer`](../../../src/Modules/Iam/DTMS.Iam.Application/Authorization/PermissionClaimsTransformer.cs) from raw `IMemoryCache` to `ITieredCache` (L1 + L2 + `dtms:cache:invalidate` broadcast).
- Change the four admin invalidation sites from `cache.Remove(...)` to `ITieredCache.InvalidateAsync(...)`: [`IamEndpoints.cs:212,252`](../../../src/Modules/Iam/DTMS.Iam.Presentation/IamEndpoints.cs), [`SystemAdminEndpoints.cs:628,661`](../../../src/Modules/Iam/DTMS.Iam.Presentation/SystemAdminEndpoints.cs).
- Fix a pre-existing gap: the client **deactivate** handler ([`SystemAdminEndpoints.cs:203-221`](../../../src/Modules/Iam/DTMS.Iam.Presentation/SystemAdminEndpoints.cs)) never invalidates `CachedSystemClientReader` (which has zero callers today), so a deactivated client stays authenticatable until the 5-min L2 TTL lapses. Add the `InvalidateAsync` call.

**Closes:** #7 (permission grants/revokes and deactivations propagate cross-pod within seconds).

### Phase 4 — Deferred (separate PRs; external dependencies)

| Work | Addresses | Why deferred |
|---|---|---|
| Retire `SystemClientAuthMiddleware`; `/source/*` uses the SystemJwt scheme directly | Altitude — one validation path instead of two | Move active/credential checks into the scheme first (Phase 2 does most of this) |
| Per-identity rate limiting — partition [`Program.cs:655`](../../../src/DTMS.Api/Program.cs) by `sub` / `client_id` after auth, not by IP | Self-DoS: the Next BFF proxies all users from one IP, so per-IP limiting throttles all users together | Requires moving from a `GlobalLimiter` to an endpoint-level limiter that runs after authentication |
| Enable `aud` validation on UserJwt | #2 residual (audience binding for user tokens) | Blocked on the External Auth team adding `iss`/`aud` claims (ADR-014 open question) |

## Reasoning — Why a `"Bearer"` selector over per-endpoint re-pinning

An alternative was to introduce explicit `RequireUser(...)` / `RequireSystem(...)` / `RequireUserOrSystem(...)` helpers and re-pin all ~164 endpoints. Rejected because:

- **Blast radius.** Re-pinning 164 security-sensitive call sites across 8 modules is high-risk churn for no behavioural gain — permission scoping already decides who may call what (Phase S.8b: "permission scope is the boundary").
- **A multi-audience helper reintroduces defect #3.** `RequireUserOrSystem` would list two schemes in one policy, running both handlers per request. The selector avoids this: one scheme name, forwarded to one handler.
- **Backward compatibility.** Keeping the name `"Bearer"` means zero endpoint edits and a trivially revertible Phase 1.

Explicit `RequireSystem`/`RequireUser` remain available as **optional defense-in-depth** on individual sensitive endpoints later, but are not required for the security boundary.

The selector routes on the token's `kid` / `sub` **before** validation. This is safe: `kid` and `sub` are inside the signed payload, so an attacker cannot change them without invalidating the signature, and a misrouted token simply fails against the chosen handler's single key — misrouting causes rejection, never a downgrade.

## Implementation Sketch — order & scope

- **PR 1 = Phase 1 + Phase 2** (ship together — Phase 1's SystemJwt scheme depends on Phase 2's revocation check to close defect #1 properly).
  - Touch: `Program.cs` (auth wiring), new `AuthSchemes.cs`, new `CachedSystemJwtRevocationList`, DI registration in `ModuleServiceRegistration.cs`.
  - No endpoint files change.
- **PR 2 = Phase 3.** Touch: `PermissionClaimsTransformer.cs`, four admin invalidation sites, deactivate handler.
- **PR 3+ = Phase 4 items,** each independently.

### Pre-implementation check

The comment at [`SystemJwtValidator.cs:25-27`](../../../src/Modules/Iam/DTMS.Iam.Application/Authorization/SystemJwtValidator.cs) still claims per-URL system-key matching "happens in the middleware" — this is stale (removed in S.8e). Before planning the Phase 4 middleware retirement, grep `SystemClientAuthMiddleware` for any route/`{key}` read to confirm none remains.

## Verification

1. **Regression** — `dotnet test`; all existing endpoint tests green (proves the `"Bearer"` rename is transparent).
2. **User path** — frontend login → `GET /api/v1/*` → 200 (routed to UserJwt).
3. **System path** — `POST /oauth/token` (client_credentials) → call an endpoint the client has permission for → 200; one it lacks → 403.
4. **Revocation on a generic endpoint (the key test)** — mint a system token, call a generic `[Authorize]` endpoint → 200; `RevokeAsync(jti)`; call again → **401 within ~30 s**. Before Phase 1 this call still succeeds until expiry — that contrast proves defect #1 is closed.
5. **Cross-pod permission** (multi-replica) — grant/revoke a permission via pod A → exercise via pod B → change visible within seconds.
6. **Selector negatives** — malformed token / forged `kid` → routed to UserJwt → 401, no crash.
7. **Redis-blip resilience** (Phase 2) — with a warm negative cache, briefly drop Redis → in-flight system requests still authenticate until the cache TTL lapses.

## Consequences

### Positive
- ✅ Revoked system tokens stop working on **all** endpoints within the cache TTL, not up to 365 days.
- ✅ Cryptographic separation of the two issuers at the scheme layer (one token → one key).
- ✅ Zero endpoint edits in Phase 1; trivially revertible.
- ✅ Hot path drops from two RSA verifies to one for system tokens; revocation is async + cached.
- ✅ Permission and deactivation changes propagate cross-pod in seconds, reusing existing `ITieredCache` infrastructure.

### Negative
- ❌ Two `AddJwtBearer` registrations duplicate some options (ClockSkew, hub events) — minor, factor into a shared helper.
- ❌ A ~15–30 s window where a freshly revoked token is still accepted (negative-cache TTL) — an explicit availability/latency trade against a per-request Redis round-trip.
- ❌ UserJwt `aud` remains unvalidated until the External Auth team ships `iss`/`aud` (tracked in Phase 4).

### Neutral
- 🟡 `SystemClientAuthMiddleware` and the `/api/v1/source/*` branch remain until Phase 4; the generic path and the source path both validate + revocation-check in the interim (minor duplication).
- 🟡 Per-IP rate limiting stays until Phase 4; unaffected by the scheme split.
