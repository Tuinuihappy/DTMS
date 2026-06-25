# ADR-014: Mobile API Authentication — External Auth API + Role-Based Policy

- **Status**: Accepted
- **Date**: 2026-06-25
- **Deciders**: Solo dev (DTMS)
- **Supersedes**: [ADR-007 Mobile API Authentication (DTMS internal auth)](adr-007-mobile-api-authentication.md)
- **Related**: [ADR-012 PWA Mobile Stack](adr-012-operator-mobile-stack-pwa.md), [ADR-013 Push Notification](adr-013-push-notification-web-push.md)

## Context

ADR-007 (2026-06-22) decided to build a **DTMS internal auth service** with custom JWT + audience separation + device-bound refresh tokens. The decision was made before discovering that an **External Auth API already exists at `http://10.204.212.28:15000/auth/login`** — a corporate identity provider that:

- Accepts `username + password` POST
- Returns: `employeeCode`, `displayName`, `role` (e.g. "Admin"), `thumbnailPhoto`, `token` (JWT)
- Serves **all employees** including warehouse operators (single user store)

**Confirmed by user (2026-06-25):** "operator ใช้ login เดียวกัน" — operators authenticate against the same External Auth API as dispatcher console users.

This eliminates the need for DTMS to:
- Issue its own JWT
- Store passwords / handle password reset
- Maintain refresh token rotation
- Implement biometric / 2FA flows
- Handle account lockout / lifecycle

DTMS becomes a **JWT consumer** (validates tokens issued by External Auth) instead of a **JWT issuer**.

## Decision

Use **External Auth API as the sole identity provider** for both dispatcher console and operator PWA. DTMS API validates incoming JWTs and enforces authorization via **role-based ASP.NET policies**. DTMS maintains a thin `Operator` aggregate that auto-creates on first login (syncing identity from JWT claims) for storing DTMS-specific operator data (warehouse scope, certifications, push subscriptions, etc.).

### Authentication flow
```
1. Operator opens PWA → /m/login
2. PWA submits credentials → POST http://10.204.212.28:15000/auth/login
3. External Auth returns JWT (with employeeCode, displayName, role claims)
4. PWA stores JWT in IndexedDB (encrypted via Web Crypto API)
5. PWA calls DTMS API: Authorization: Bearer <JWT>
6. DTMS validates JWT signature + expiry + claims via JwtBearer middleware
7. On first call, DTMS auto-creates Operator aggregate from claims
8. Subsequent calls use existing Operator record
```

### Authorization (ASP.NET policy-based)
```csharp
services.AddAuthorization(options =>
{
    options.AddPolicy("OperatorOnly", p => p.RequireRole("Operator"));
    options.AddPolicy("AdminOnly",    p => p.RequireRole("Admin"));
    options.AddPolicy("OperatorOrAdmin", p => p.RequireRole("Operator", "Admin"));
    
    // Future Phase 5+: warehouse-scoped (operator can only act on their warehouse)
    options.AddPolicy("OperatorAtWarehouse", p =>
        p.RequireRole("Operator").RequireClaim("warehouseId"));
});

// Endpoint guards
group.MapPost("/operator/trips/{id}/pickup", ...)
     .RequireAuthorization("OperatorOrAdmin");

group.MapGet("/dispatch/trips", ...)
     .RequireAuthorization("AdminOnly");
```

### Operator aggregate (Phase 4.1)
```csharp
public class Operator
{
    public Guid Id { get; }                        // DTMS internal Guid
    public string EmployeeCode { get; }            // FK to External Auth user (unique)
    public string DisplayName { get; private set; } // Synced from JWT on each login
    public string Role { get; private set; }       // Synced from JWT
    public Guid? CurrentTripId { get; }            // DTMS-managed
    public IReadOnlyList<Certification> Certifications { get; }  // hazmat, cold-chain
    public IReadOnlyList<OperatorDevice> Devices { get; }        // push subscriptions
    public Guid? PrimaryWarehouseId { get; }       // optional scope
    public bool IsActive { get; }                  // DTMS-managed soft-delete

    public static Operator CreateFromJwtClaims(
        string employeeCode, string displayName, string role)
    {
        return new Operator { /* ... */ };
    }

    public void SyncFromJwtClaims(string displayName, string role)
    {
        // First-write-wins for EmployeeCode (PK-like)
        // Last-write-wins for displayName + role (External Auth = source of truth)
        DisplayName = displayName;
        Role = role;
    }
}
```

### Auto-create on first login (middleware)
```csharp
public class OperatorSyncMiddleware
{
    public async Task InvokeAsync(HttpContext context, IOperatorRepository repo)
    {
        if (context.User.Identity?.IsAuthenticated != true) return;
        
        var employeeCode = context.User.FindFirst("employeeCode")?.Value;
        var displayName = context.User.FindFirst("displayName")?.Value;
        var role = context.User.FindFirst(ClaimTypes.Role)?.Value;
        if (employeeCode is null) return;
        
        var op = await repo.GetByEmployeeCode(employeeCode);
        if (op is null)
        {
            op = Operator.CreateFromJwtClaims(employeeCode, displayName!, role!);
            await repo.AddAsync(op);
        }
        else
        {
            op.SyncFromJwtClaims(displayName!, role!);  // updates DisplayName + Role
            await repo.UpdateAsync(op);
        }
        
        // Stash DTMS Operator.Id in context for downstream handlers
        context.Items["OperatorId"] = op.Id;
        await _next(context);
    }
}
```

## Reasoning — Why External Auth over DTMS internal

### What changed since ADR-007
ADR-007 was written without knowledge of External Auth API. Now that we know:
- Auth API exists + works + serves operators
- Builds, deploys, ops are already handled by another team
- Reusing eliminates duplicate user store / password management

### DTMS internal auth (ADR-007) would have required
- Build login + password storage + reset flow (~2 weeks dev)
- Implement refresh token rotation + revocation (~1 week)
- Implement device binding + 2FA (~1 week)
- Maintain in perpetuity (security patches, password policy updates)
- Solve "where do operator credentials come from?" (HR sync? manual provisioning?)

External Auth API eliminates all the above — **net savings: ~4 weeks dev + ongoing maintenance**.

### Single user store benefits
- Operator who is also an Admin (e.g., supervisor) = same login, role determines permissions
- Password reset = handled in External Auth (corporate workflow already exists)
- Account deactivation = External Auth = effective immediately (DTMS JWT validates expiry)
- Audit = External Auth login events already captured by corporate IAM

### Audience separation — not needed
ADR-007 proposed `aud: "operator-app"` vs `aud: "dispatcher-console"` claim separation. With single user store + role claim, **the role itself is the access boundary** — `role: "Operator"` cannot call admin endpoints (policy denies). No audience claim needed.

If future enterprise customers require strict per-app token isolation, External Auth team can issue audience claims OR DTMS adds a thin `OperatorAppOnly` policy. Not Phase 4 scope.

## Implementation Sketch

### DTMS API config
```csharp
// Program.cs
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Try auto-discovery first (if External Auth exposes .well-known/openid-configuration)
        options.Authority = "http://10.204.212.28:15000";
        options.RequireHttpsMetadata = false;  // dev only; true in production
        
        // If no JWKS endpoint, fall back to manual public key:
        // options.TokenValidationParameters = new TokenValidationParameters {
        //     ValidIssuer = "external-auth",
        //     IssuerSigningKey = new RsaSecurityKey(loadPublicKeyFromConfig()),
        //     ValidateAudience = false,  // External Auth doesn't issue aud claim per our use
        // };
    });

builder.Services.AddAuthorization(/* policies as above */);

app.UseMiddleware<OperatorSyncMiddleware>();  // after auth, before endpoints
```

### PWA login flow
```typescript
async function login(username: string, password: string) {
    const res = await fetch('http://10.204.212.28:15000/auth/login', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ username, password }),
    });
    
    if (!res.ok) throw new Error('Login failed');
    const { token, employeeCode, displayName, role } = await res.json();
    
    // Store JWT encrypted in IndexedDB
    await storeEncryptedToken(token);
    
    // Cache user info for UI (non-sensitive)
    sessionStorage.setItem('user', JSON.stringify({ employeeCode, displayName, role }));
    
    // Register push subscription (per ADR-013)
    if (role === 'Operator') {
        await registerForPush(token);
    }
}
```

### CORS at External Auth
**Required**: External Auth API must allow CORS origin from PWA domain. Coordinate with External Auth team to add:
- `Access-Control-Allow-Origin: https://dtms.example.com`
- `Access-Control-Allow-Methods: POST`
- `Access-Control-Allow-Headers: Content-Type`

If CORS is not feasible at External Auth → fallback to DTMS API proxy (DTMS API endpoint that forwards to External Auth). This adds 1 hop but isolates CORS concerns.

## Open questions to confirm with External Auth team

1. **JWKS endpoint** — Does `http://10.204.212.28:15000/.well-known/jwks.json` exist? If yes, JwtBearer auto-discovers signing keys. If no, need public key file.
2. **Signing algorithm** — RS256 (asymmetric, preferred) or HS256 (shared secret)? Affects key distribution.
3. **Token lifetime** — How long is the JWT valid? Need to know for PWA refresh strategy.
4. **Refresh token** — Does External Auth support refresh? If no, PWA must re-prompt login when token expires.
5. **CORS** — Can External Auth allow CORS from PWA domain? Or do we proxy through DTMS API?
6. **Claims schema** — Are claim names standard (`role`, `sub`, `aud`) or custom? Need to align middleware claim lookups.
7. **Operator-specific claims** — Can External Auth include `warehouseId` or other org-scope claims? (For ADR-006 transport mode policy + future per-warehouse policies.)

These should be answered before Phase 4.2 (mobile API endpoints) starts.

## Consequences

### Positive
- ✅ ~4 weeks dev saved (no DTMS internal auth to build)
- ✅ No password storage / reset flow / 2FA in DTMS scope
- ✅ Single source of truth for user identity (External Auth)
- ✅ Account deactivation propagates instantly (External Auth = enterprise IAM)
- ✅ Audit trail in External Auth's existing IAM system

### Negative
- ❌ Dependency on External Auth uptime (mitigated: existing service, known SLA)
- ❌ Less control over token lifetime / refresh policy (External Auth team controls)
- ❌ Cannot enforce DTMS-specific password policy (delegated to External Auth)
- ❌ Need to coordinate CORS or proxy decision with External Auth team

### Neutral
- 🟡 DTMS still maintains `Operator` aggregate for DTMS-specific data (warehouse scope, certs, push subscriptions) — minimal but non-zero
- 🟡 Future enterprise customers wanting separate operator IdP → add 2nd JwtBearerScheme (small refactor)

## Why this supersedes ADR-007

ADR-007 was built on the assumption that DTMS would issue its own JWTs. Discovery of External Auth API (already serving operators) eliminates that assumption and the entire downstream design (refresh tokens, device binding, audience separation, account lifecycle). Reusing External Auth is strictly simpler + faster + more secure (one less identity system to attack).

The interface concepts from ADR-007 (`OperatorAppPolicy`, claim-based authorization) translate to ADR-014's role-based policies — same security boundary, different implementation.
