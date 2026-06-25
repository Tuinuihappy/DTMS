# ADR-007: Mobile API Authentication Strategy

- **Status**: ⚠️ **Superseded by [ADR-014 Mobile API Authentication — External Auth + Role-Based Policy](adr-014-mobile-api-authentication-external-auth.md)** (2026-06-25)
- **Date**: 2026-06-22
- **Deciders**: Architecture team
- **Related**: [Phase 4](../phases/phase-4-transport-manual.md), [Manual Operator API](../api/manual-operator-api.md), [ADR-005](adr-005-push-notification-gateway.md)

> **Superseded notice (2026-06-25):** ADR-007 assumed DTMS would issue its own JWTs with audience separation + device-bound refresh tokens. Discovery on 2026-06-25 that an **External Auth API** (`http://10.204.212.28:15000`) already serves both dispatchers and operators with a single user store eliminates the need for DTMS internal auth. See [ADR-014](adr-014-mobile-api-authentication-external-auth.md) for current decision — JWT consumer pattern + role-based policies + DTMS Operator aggregate for app-specific data.

## Context

Phase 4 introduces mobile API (`/api/operator/*`) for operator app. Authentication requirements ต่างจาก dispatcher console:

| Concern | Dispatcher Console (existing) | Operator Mobile App (new) |
|---|---|---|
| **Client type** | Browser (web) | Native app (iOS/Android) |
| **Session duration** | ~8 hours (work shift) | Full shift + offline gaps |
| **Auth method** | Username + password + 2FA | EmployeeCode + password initially; biometric thereafter |
| **Token storage** | HTTP-only cookie | Secure storage (Keychain/Keystore) |
| **Token lifetime** | Short (15 min) + refresh | Longer access (1 hr) + long refresh (30 days) |
| **Device binding** | Not required | Required (1 operator = N devices, all tracked) |
| **Logout impact** | Cookie clear | Token revoke + push token deregister |
| **Lost device** | Re-login from new browser | Remote revoke + new device pair |

ปัญหาที่ต้องตัดสิน:
1. Auth protocol — JWT custom? OAuth 2.0 ROPC? OIDC?
2. Identity provider — DTMS internal? External (Auth0, Azure AD B2C, Keycloak)?
3. Token strategy — single token? access + refresh? sliding session?
4. Device binding — JWT claim? separate DB row? both?
5. Audience separation — same JWT for web + mobile? separate?
6. Offline behavior — tokens valid for X hours offline?
7. Push token coupling — login = register push token?

## Decision

ใช้ **JWT with audience separation** + **device-bound refresh tokens** ออก issue โดย DTMS internal auth service:

### Token Strategy

```
Login:
  POST /api/operator/auth/login (employeeCode + password + deviceFingerprint + pushToken)
  → returns: { accessToken (1 hr), refreshToken (30 days), expiresAt }

Use access token:
  Authorization: Bearer <accessToken>
  Claims: { sub: operatorId, aud: "operator-app", device_id: <uuid>, exp: ... }

Refresh before expiry:
  POST /api/operator/auth/refresh (refreshToken + deviceFingerprint)
  → returns: new { accessToken, refreshToken }
  (rolling refresh — each refresh issues new refresh token, invalidates old)

Logout:
  POST /api/operator/auth/logout (revokes refresh token + clears push token)

Lost device:
  POST /api/operator/auth/revoke-device (by dispatcher / admin)
  → invalidates all refresh tokens for that device
```

### Audience Separation (Critical)

JWTs include `aud` claim distinguishing client:
- `aud: "operator-app"` — issued to mobile app, accepted by `/api/operator/*`
- `aud: "dispatcher-console"` — issued to web UI, accepted by `/api/*` (non-operator)
- `aud: "system"` — for service-to-service (background workers, integration events)

```csharp
// in Program.cs
builder.Services.AddAuthorization(opts =>
{
    opts.AddPolicy("OperatorAppPolicy", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireClaim("aud", "operator-app");
        policy.RequireClaim("device_id");      // mobile must have device binding
    });

    opts.AddPolicy("DispatcherPolicy", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireClaim("aud", "dispatcher-console");
    });
});

// Endpoint guards
group.MapPost("/trips/{id}/pickup", ...)
     .RequireAuthorization("OperatorAppPolicy");
```

**Why audience separation matters:** Operator app JWT ห้ามใช้เปิด dispatcher console (different permissions, different attack surface). ถ้า attacker ขโมย operator JWT → restricted to mobile API only

### Device Binding

```csharp
// Login flow
POST /api/operator/auth/login
{
  "employeeCode": "EMP-001",
  "password": "...",
  "deviceFingerprint": "iPhone14-A1B2C3D4-installID",   // app-generated stable ID
  "deviceModel": "iPhone 14 Pro",
  "appVersion": "1.0.3",
  "pushToken": "fcm-token-xxx"
}

// Server side:
1. Validate credentials
2. Find/create OperatorDevice row (operator_id + device_fingerprint = UK)
3. Update push_token + last_seen_at
4. Generate JWT with claim "device_id": <OperatorDevice.Id>
5. Issue refresh token bound to device_id (refresh_tokens table FK)
```

```sql
CREATE TABLE transport_manual.refresh_tokens (
    id UUID PRIMARY KEY,
    token_hash CHAR(64) NOT NULL UNIQUE,   -- SHA-256 of token
    operator_id UUID NOT NULL REFERENCES transport_manual.operators(id),
    device_id UUID NOT NULL REFERENCES transport_manual.operator_devices(id),
    issued_at TIMESTAMPTZ NOT NULL,
    expires_at TIMESTAMPTZ NOT NULL,
    revoked_at TIMESTAMPTZ,
    revoked_reason VARCHAR(200)
);

CREATE INDEX ix_refresh_tokens_operator_active
    ON transport_manual.refresh_tokens(operator_id, device_id)
    WHERE revoked_at IS NULL AND expires_at > NOW();
```

**Why device binding:**
- Stolen JWT alone ไม่พอ — need device_fingerprint match
- Revoke device = revoke all tokens for that device
- Audit: which device made each API call
- Lost phone scenario: admin can revoke specific device without re-auth other devices

### Refresh Token Rolling

```csharp
// Each refresh issues new refresh token AND invalidates the old one
public async Task<TokenPair> RefreshAsync(string refreshToken, string deviceFingerprint, CancellationToken ct)
{
    var hash = Sha256(refreshToken);
    var existing = await _refreshTokens.GetByHashAsync(hash, ct);

    if (existing is null || existing.RevokedAt is not null || existing.ExpiresAt < DateTime.UtcNow)
        throw new SecurityException("Invalid refresh token");

    var device = await _devices.GetAsync(existing.DeviceId, ct);
    if (device.DeviceFingerprint != deviceFingerprint)
        throw new SecurityException("Device mismatch");   // potential token theft

    // Invalidate old
    existing.Revoke("rotated");
    await _refreshTokens.UpdateAsync(existing, ct);

    // Issue new pair
    var newAccess = GenerateAccessToken(existing.OperatorId, device.Id);
    var newRefresh = GenerateRefreshToken();
    await _refreshTokens.AddAsync(new RefreshToken(
        hash: Sha256(newRefresh),
        operatorId: existing.OperatorId,
        deviceId: device.Id,
        issuedAt: DateTime.UtcNow,
        expiresAt: DateTime.UtcNow.AddDays(30)), ct);

    return new TokenPair(newAccess, newRefresh);
}
```

**Why rolling refresh:** if attacker uses leaked refresh token, victim's next refresh fails → victim contacts support → device revoked. Detection mechanism.

### Offline Token Validity

- Access token: 1 hour (validate offline using JWT signature)
- Refresh token: 30 days (server validation required)
- Offline operation: app can use cached access token until expiry; queue refresh + API calls for sync
- After 1 hour offline: API calls queue locally; refresh attempt on connectivity restored

## Alternatives Considered

### Alternative A: OAuth 2.0 ROPC (Resource Owner Password Credentials)

Standard OAuth flow with password grant

**Pros:**
- Standard protocol
- Library support (Microsoft.AspNetCore.Authentication.OAuth)

**Cons:**
- ROPC discouraged by OAuth spec (only for legacy clients)
- Doesn't add value vs custom JWT for trusted first-party app
- ทำให้ flow ซับซ้อนกว่าจำเป็น

**Rejected because:** Mobile app is first-party — no need for OAuth's "give third-party limited access" model

### Alternative B: OAuth 2.0 PKCE (mobile-friendly flow)

Authorization Code with PKCE — used by most consumer apps

**Pros:**
- Industry standard for mobile
- Browser-based login (better UX for SSO future)
- No password handling in app

**Cons:**
- Requires browser redirect (not great for embedded login UX)
- Complexity ไม่คุ้มสำหรับ first-party enterprise app
- ROPC simpler for operator workflow

**Considered for future:** If add SSO with employee Active Directory, migrate to PKCE

### Alternative C: External identity provider (Auth0, Azure AD B2C, Keycloak)

**Pros:**
- MFA, SSO, password reset built-in
- Audit + monitoring built-in
- Compliance-ready (SOC2, etc.)

**Cons:**
- Cost (per active user / month)
- External dependency
- Data residency concerns (employee PII to 3rd party)
- Setup complexity for small team
- Operator login UX should be simple (no redirect to external page)

**Rejected for now:** Revisit if scale > 1000 operators OR compliance requires it

### Alternative D: Long-lived API key per operator (no refresh)

**Pros:** Simple

**Cons:**
- No expiry = perpetual risk if leaked
- No device binding
- No audit per session

**Rejected:** Insufficient security for operations involving signed POD + GPS

### Alternative E: Session cookie (like web)

**Pros:** Familiar pattern

**Cons:**
- Native apps don't handle cookies as well as browsers
- CSRF concerns
- Doesn't compose with native mobile patterns (Keychain/Keystore)

**Rejected:** JWT in Authorization header standard for mobile

### Alternative F: Same JWT for dispatcher + mobile (no audience separation)

**Pros:** 1 token type, simpler

**Cons:**
- Stolen mobile JWT can hit dispatcher endpoints (privilege escalation)
- Violates principle of least privilege
- Hard to revoke per-client

**Rejected:** Audience separation is best practice — minimal complexity, big security win

## Implementation Details

### JWT Claims (Operator App)

```json
{
  "sub": "<operatorId>",
  "aud": "operator-app",
  "iss": "https://api.dtms.com",
  "exp": 1719073200,
  "iat": 1719069600,
  "device_id": "<deviceUuid>",
  "operator_code": "EMP-001",
  "shift_id": "<currentShiftId>",
  "warehouse_scope": ["wh-id-1", "wh-id-2"],
  "certifications": ["STANDARD", "HAZMAT"]
}
```

**Why include shift + scope in JWT:**
- Geofence check needs warehouse scope → avoid DB lookup per request
- Trade-off: shift change requires token refresh (acceptable)

### Token Signing

- **Algorithm**: RS256 (asymmetric — public key for verification, private key in Key Vault)
- **Key rotation**: every 90 days, overlap period of 7 days (both keys valid)
- **JWKS endpoint**: `/.well-known/jwks.json` (mobile app can fetch + cache)

### Password Hashing

- Algorithm: Argon2id (memory-hard, modern best practice)
- Parameters: 64MB memory, 3 iterations, 4 parallelism
- Library: `Konscious.Security.Cryptography.Argon2`

### Account Lockout

- 5 failed attempts in 15 minutes → 30-minute lockout
- Distributed counter (Redis) — same lockout across multiple API instances
- Admin can unlock via `/api/admin/operators/{id}/unlock`

### Rate Limiting (per [Manual Operator API](../api/manual-operator-api.md#rate-limits))

| Endpoint | Limit | Bucket key |
|---|---|---|
| `/auth/login` | 5/min | device_fingerprint |
| `/auth/refresh` | 10/hour | refresh_token |
| All authenticated | 30/min | operator_id |
| `/presence/heartbeat` | 4/min | operator_id |

### Logout Side Effects

```csharp
POST /api/operator/auth/logout

1. Validate access token
2. Revoke all refresh tokens for current device
3. Clear push_token on OperatorDevice (so server stops sending push)
4. Emit AuditEvent: OperatorLoggedOut
5. Return 200
```

### Lost Device Flow

```csharp
// Dispatcher / admin endpoint (DispatcherPolicy)
POST /api/admin/operators/{operatorId}/devices/{deviceId}/revoke
Body: { reason: "device-lost" }

1. Mark OperatorDevice.IsActive = false
2. Revoke all refresh tokens for that device
3. Clear push_token
4. Audit log: device_id, revoked_by_user, reason
```

After revoke: operator must re-login on new device (with new fingerprint → new OperatorDevice row)

## Security Considerations

### Token Theft Detection

- Refresh token mismatch (device fingerprint changed) → revoke + alert
- Concurrent refresh from different IPs → flag for review
- Failed login spike per device → temporary block

### Sensitive Data in Tokens

- ❌ Don't include: full name, phone, email (in token claims)
- ✅ Include: operator_id (UUID), employee_code (lookup key), scope IDs
- App fetches details from `/api/operator/me` if needed (revocable, fresh)

### TLS Pinning (Mobile App Side)

Recommended for production builds: pin DTMS API certificate in app to prevent MITM via rogue WiFi

### Biometric Unlock (Mobile App Side)

After initial password login, app may unlock with biometric (Face ID / Touch ID / fingerprint):
- Biometric protects access to **stored** access token in Keychain/Keystore
- Server doesn't know about biometric — just sees valid JWT
- App responsibility: re-prompt password every 7 days or after token full refresh chain expires

## Audit Logging

All auth events logged to `audit.auth_events` table:

| Event | Captured Data |
|---|---|
| Login success | operator_id, device_id, ip, user_agent, geo |
| Login failure | employee_code, device_id, ip, reason |
| Refresh | operator_id, device_id, refresh_token_id (old + new) |
| Logout | operator_id, device_id |
| Device revoked | operator_id, device_id, revoked_by, reason |
| Lockout triggered | employee_code, ip, attempt_count |

Retention: 1 year minimum, queryable for compliance

## Consequences

### Positive

- ✓ Audience separation prevents cross-client privilege escalation
- ✓ Device binding makes token theft significantly harder
- ✓ Rolling refresh enables theft detection
- ✓ Offline-capable (1 hour cached access token)
- ✓ Self-contained — no external IdP dependency
- ✓ JWT validation distributed (no central session store needed for access token)

### Negative

- ✗ Refresh token table requires cleanup job (expired rows)
- ✗ Key rotation requires app awareness (re-fetch JWKS)
- ✗ Lost device = operator can't work until admin revokes (acceptable, infrequent)
- ✗ Build auth service ourselves vs. SaaS IdP (more code to maintain)

### Neutral

- Biometric unlock = app concern, not server
- SSO with corporate AD = future migration to OIDC (PKCE flow)

## Migration / Rollout

### Phase 4 Implementation
1. Create `transport_manual.operator_devices` + `refresh_tokens` tables (in Phase 4 migration)
2. Implement `OperatorAuthService` (login, refresh, revoke)
3. Configure JWT signing with key from Key Vault
4. Implement `/api/operator/auth/*` endpoints
5. Implement `OperatorAppPolicy` authorization policy
6. Add audit logging (auth_events table)
7. Build admin endpoint for device revocation

### Mobile App Setup (separate project)
1. Secure storage integration (Keychain / Keystore)
2. Auto-refresh logic (refresh 5 min before expiry)
3. Offline queue for API calls during disconnection
4. Biometric unlock for stored tokens
5. TLS pinning for production builds

## Acceptance Criteria

- [ ] `/api/operator/auth/login` issues JWT with `aud=operator-app` + `device_id` claims
- [ ] `OperatorAppPolicy` rejects tokens without correct audience
- [ ] Refresh tokens stored hashed (SHA-256), rotation works
- [ ] Device fingerprint mismatch on refresh → token revoked
- [ ] Account lockout after 5 failures in 15 min
- [ ] Admin can revoke specific device
- [ ] Audit log captures all auth events
- [ ] Rate limits enforced per [Manual Operator API](../api/manual-operator-api.md#rate-limits)

## References

- JWT best practices: RFC 8725
- OAuth 2.0 RFC 6749
- OWASP Authentication Cheat Sheet
- [Manual Operator API — Authentication](../api/manual-operator-api.md#authentication)
- [Phase 4 — Mobile Audience JWT Setup](../phases/phase-4-transport-manual.md#step-11-mobile-audience-jwt-setup)
