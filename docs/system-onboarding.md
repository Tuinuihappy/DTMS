# Source-System Onboarding Runbook

DTMS authenticates federated source systems (OMS, SAP, WMS adapters,
etc.) with **OAuth 2.0 client_credentials** (RFC 6749 §4.4). Each system
holds a long-lived `client_secret`, exchanges it for a short-lived JWT
(1 hour default) via `POST /oauth/token`, then sends the JWT as
`Authorization: Bearer <token>` on inbound API calls.

This document covers:

1. Production launch checklist (first-time setup)
2. Onboarding a new partner
3. Rotating a `client_secret`
4. Generating / rotating the system signing keypair
5. Troubleshooting

---

## 1. Production launch checklist (one-time)

| # | Task | Owner | Verify |
|---|---|---|---|
| 1 | Generate RSA-2048 system signing keypair (see §4) | DevOps | `openssl rsa -in system-jwt.key -text -noout` shows 2048-bit |
| 2 | Set `Jwt__SystemSigningPrivateKey` + `Jwt__SystemSigningPublicKey` in production secret store (Docker Compose `.env` or external vault) | DevOps | `docker compose config` shows env vars set (don't echo values) |
| 3 | Deploy DTMS image + restart api container | DevOps | `docker compose ps` → api Healthy |
| 4 | Smoke `/oauth/token` with dummy credentials (should return 401 invalid_client, NOT 500) | DevOps | `curl -X POST .../oauth/token -d '...' → 401` proves keypair loaded |
| 5 | Onboard first partner (e.g. OMS) — see §2 | Admin | partner integration test passes |
| 6 | Set monitoring on 401 rate of `/api/v1/source/*` + token endpoint | DevOps | dashboards live |

If step 4 returns 500 → keypair PEM is malformed or env var not set.
See [troubleshooting §5](#5-troubleshooting).

---

## 2. Onboarding a new partner

### 2.1 Via the admin UI (recommended)

1. Navigate to **Admin → IAM → Systems**
2. Click **New system**
3. Fill in key (lowercase slug, e.g. `oms`) / display name / owner contact
4. Click **Create system**
5. The one-time secret banner shows the `client_secret` plaintext —
   **copy it before dismissing**. The backend stores only the SHA-256
   hash; the plaintext is unrecoverable after the banner closes.
6. Click **Test this credential** — the proxy handler runs the full
   OAuth flow (`POST /oauth/token` → use returned JWT to call
   `/source/{key}/whoami`) and reports back whether auth works end-to-end.
7. Send the `client_secret` to the partner operator via a secure channel
   (1Password, Bitwarden, encrypted email — never plain Slack/email).

### 2.2 Via the HTTP API (scripting bulk onboarding)

```bash
curl -X POST https://dtms.internal/api/v1/iam/systems \
  -H "Authorization: Bearer <your-admin-jwt>" \
  -H "Content-Type: application/json" \
  -d '{
    "key": "acme",
    "displayName": "ACME Logistics",
    "ownerContact": "ops@acme.com",
    "isActive": true
  }'
```

Response (plaintext returned exactly once):
```json
{
  "key": "acme",
  "displayName": "ACME Logistics",
  "permissions": ["dtms:source:acme:order:write", "dtms:source:acme:order:read"],
  "authScheme": "bearer-jwt",
  "secret": "dtms_cs_acme_xK9pQ2vR8nL4mT7wY3jH6sB1aZ5cE0fD"
}
```

### 2.3 Partner integration

```bash
# Step 1 — exchange client_secret for an access token (cache for ~55 min)
curl -X POST https://dtms.internal/oauth/token \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "grant_type=client_credentials&client_id=acme&client_secret=dtms_cs_acme_..."

# Response:
# { "access_token": "eyJ...", "token_type": "Bearer", "expires_in": 3600 }

# Step 2 — use the access_token on every API call. Identity is derived
# from the JWT sub claim, not the URL — the endpoint has no {key} segment.
curl -X POST https://dtms.internal/api/v1/source/delivery-orders \
  -H "Authorization: Bearer eyJ..." \
  -H "Content-Type: application/json" \
  -d '{...order payload...}'
```

Partner code outline (any language):
```
let token = null, expiry = 0
async function call(path, body) {
  if (!token || Date.now() > expiry - 60_000) {  // refresh 1 min early
    const r = await fetch(TOKEN_URL, { method: 'POST', body: ... })
    const j = await r.json()
    token = j.access_token
    expiry = Date.now() + j.expires_in * 1000
  }
  return fetch(path, { headers: { Authorization: `Bearer ${token}` }, body })
}
```

**Performance note**: token endpoint expects ≈ 1 request per partner per
hour (one per token lifetime). Partners refreshing every call indicates
a missing token cache — investigate before it becomes a load issue.

---

## 2.5 Admin-issued long-lived JWT (escape hatch)

For partners that cannot run an OAuth client (legacy software, no HTTP
library that supports POST `application/x-www-form-urlencoded`, or just
"please give me one token I'll paste into a config file"), the admin UI
can mint a JWT directly with a custom lifetime.

### When to use this vs the standard OAuth flow

| Use OAuth `/oauth/token` (default) | Use admin-issued JWT |
|---|---|
| Partner can run an HTTP client + cache logic | Partner is a script, a curl command, or legacy software |
| Blast radius if leaked ≤ 1 hour matters | Operational simplicity > short blast radius |
| Standards-based integration is required | Internal tool, low-stakes integration |

### Procedure

1. **Admin → IAM → Systems → click the system row**
2. In the Credential card click **Issue JWT**
3. Pick a lifetime (presets: 7 / 30 / 90 / 180 / 365 days; default 90)
4. Click **Issue token** — banner shows the JWT plaintext (`eyJ...`)
5. Click **Test this credential** — confirms the JWT authenticates
   against `/source/whoami` directly (no OAuth round-trip)
6. Send the JWT to the partner via a secure channel
7. Partner uses it directly:

```bash
curl -X POST https://dtms.internal/api/v1/source/delivery-orders \
  -H "Authorization: Bearer eyJhbGciOiJSUzI1NiIs..." \
  -H "Content-Type: application/json" \
  -d '{...order payload...}'
```

### Trade-offs you accept

| Risk | Mitigation |
|---|---|
| JWT leaked → usable for full lifetime (up to 365 days) | Pick the shortest lifetime your partner can cope with. Rotate the signing keypair (§4.3) for a wholesale revoke |
| No per-token revocation | Deactivate the system (kills all its JWTs in ≤ 1 minute via cache invalidation) or rotate the signing keypair (revokes globally) |
| Admin needs to re-issue + re-distribute manually before expiry | Calendar the expiry date; set a reminder a week ahead |

### What "admin-issued" means at the protocol level

Nothing different from a `/oauth/token`-minted token — same RS256
signature with the same keypair, same `iss`/`aud`/`sub` claims, same
middleware validation path. The only differences are:

- **Who clicked the issue button** (admin via UI vs partner via HTTP POST)
- **Lifetime** (admin's pick vs `Jwt__SystemTokenLifetimeSeconds` default)
- **Audit trail** (`system-token-issued` action with admin's identity vs
  `/oauth/token` access logs with no per-issue audit entry)

The partner experience is identical from the middleware's perspective —
both flows produce a `Bearer <jwt>` header that validates the same way.

### Revoking an admin-issued JWT (Phase S.8c)

Every click of **Issue JWT** is recorded in `iam.SystemIssuedTokens`
(audit + backing store for the revoke list). Admin UI → System detail
shows an **Issued tokens** section with all tokens for that system:

| JTI | Issued | Expires | By | Status | |
|---|---|---|---|---|---|
| abc12345…def | 2026-07-01 09:15 | 2027-07-01 09:15 | titpooja | Active | [Revoke] |
| f9012345…c88 | 2026-06-20 14:22 | 2026-09-18 14:22 | titpooja | Revoked | — |

Clicking **Revoke** does:

1. DB row → `Status = Revoked`, `RevokedAt`, `RevokedBy` filled.
2. Redis `iam:revoked-jti:{jti}` set with TTL = remaining lifetime.
3. **Next request** with that token → validator sees the Redis entry →
   returns 401 `"credential rejected"`.
4. Other tokens for the same system continue to work.

**Fail-close semantics:** if Redis is unreachable during the validator's
revocation check, the request is **rejected** rather than passed
through. Rationale: a revoked-but-leaked token slipping through during a
Redis outage would defeat the point of having revocation. This does
mean a Redis outage temporarily disables the `/api/v1/source/*` path
for bearer-jwt systems — accepted for the security guarantee.

**Rotation vs revocation:**

| Scenario | Use |
|---|---|
| Routine rotation before expiry | Issue new token → distribute → revoke old |
| Suspected leak | Revoke leaked token immediately, then issue new one |
| Employee left who held the token | Revoke, no replacement needed until partner asks |
| Compliance-driven periodic reset | Revoke all Active tokens for the system, then issue new |

**Cascade note:** hard-deleting a SystemClient (Deactivate → Delete
in admin UI) drops the `SystemIssuedTokens` rows via FK cascade. The
Redis blocklist entries still exist until their TTL expires — that's
fine because those tokens' signatures also fail (system row gone →
middleware lookup misses → 401 anyway).

---

## 3. Rotating a `client_secret`

Rotate when:
- Secret is suspected leaked
- Routine hygiene (every 12 months, or per compliance policy)
- A person who held the secret left the partner organisation

### Procedure

1. **Coordinate** with the partner — agree a maintenance window
2. Admin UI → System detail → **Rotate credential** → copy new secret
3. Send new secret to partner via secure channel
4. Partner updates their bot configuration + redeploys
5. Old secret is invalid immediately after step 2 — until partner deploys,
   their `/oauth/token` calls fail with 401 invalid_client. Active JWTs
   issued before rotation continue to work until their `exp` (≤ 1 hour)
6. Monitor `/oauth/token` 401 rate for 1 hour to confirm partner cutover

Old tokens **already in flight** validate fine — only NEW token fetches
fail. So a typical partner sees zero downtime if they pre-load the new
secret and call `/oauth/token` exactly once after rotation.

---

## 4. Generating / rotating the system signing keypair

DTMS signs system tokens with an RSA keypair separate from the External
Auth keypair (which signs user tokens). Generate **once per environment**
(dev / staging / prod) and never check the private key into source
control.

### 4.1 Generate

```bash
# RSA-2048 (recommended) — PKCS#8 PEM
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out system-jwt.key
openssl rsa -in system-jwt.key -pubout -out system-jwt.pub

# Inspect (sanity)
openssl rsa -in system-jwt.key -text -noout | head -3
# Expected: "Private-Key: (2048 bit, 2 primes)"
```

### 4.2 Wire into env (Docker Compose)

Edit `.env` (in the same directory as `docker-compose.yml`):
```bash
Jwt__SystemSigningPrivateKey="$(cat system-jwt.key)"
Jwt__SystemSigningPublicKey="$(cat system-jwt.pub)"
Jwt__SystemTokenKeyId=dtms-system-prod-v1
Jwt__SystemTokenIssuer=dtms
Jwt__SystemTokenAudience=dtms-api
Jwt__SystemTokenLifetimeSeconds=3600
```

Then `docker compose restart api` (or full `docker compose up -d` if you
also pulled a new image).

**Verify via Compose**:
```bash
docker compose exec api curl -s -X POST http://localhost:8080/oauth/token \
  -d "grant_type=client_credentials&client_id=__notreal__&client_secret=x" \
  -H "Content-Type: application/x-www-form-urlencoded"
# Expected: {"error":"invalid_client","error_description":"Unknown or inactive client."}
# (proves keypair loaded — wrong-creds path returns 401 not 500)
```

**Secret-handling discipline**:
- Delete `system-jwt.key` from the build host after pasting into `.env`
  (`shred -u system-jwt.key`)
- Back up keypair in your password manager / secret vault — losing
  the private key means re-issuing client_secrets to every partner
- For multi-host production: store in Docker Compose `secrets:` block
  (not `.env`) or upgrade to an external secret manager

### 4.3 Rotating the keypair (every 6-12 months)

1. Generate a new keypair
2. Bump `Jwt__SystemTokenKeyId` to a new value (e.g. `dtms-system-prod-v2`)
3. Update `Jwt__SystemSigningPrivateKey` + `Jwt__SystemSigningPublicKey`
   to the new values
4. Restart the api container
5. Tokens minted by the old keypair will fail validation immediately
   (no overlap window in V1) — partners will re-fetch via `/oauth/token`
   on their next call, automatically getting new-keyed tokens
6. Worst case impact: in-flight requests during the restart return 401
   one time → partner retry succeeds. Typically zero user impact.

A future enhancement (deferred) adds a `/.well-known/jwks.json` endpoint
+ overlap window so old keys validate until they expire. For now the
simple replace-and-restart works because token lifetime is only 1 hour.

---

## 5. Troubleshooting

### `401 invalid_client` from `/oauth/token`
- Partner sending wrong `client_secret` → verify in DTMS admin UI; rotate if necessary
- Partner's `client_id` doesn't match a SystemClient → check `key` spelling
- System is deactivated → reactivate before token requests will succeed

### `401 credential rejected` from `/api/v1/source/*`
- Token expired → partner's cache is stale; force a re-fetch
- Token signature invalid → DTMS keypair rotated since token was minted;
  partner needs to re-fetch (will use new key)
- Token `sub` doesn't match URL `{key}` → partner is reusing a token across
  multiple system keys, or their cache lookup has a bug. **Each system
  has its own client_secret + token — never share tokens across systems.**
- System deactivated → reactivate

### `500 server_error` from `/oauth/token`
- Credential row's `AuthConfig` JSON is malformed → fix via DB or rotate
  the credential. Check server logs for the specific JsonException
- Missing `clientSecretHash` field in AuthConfig → same fix

### Startup failure: `"PrivateKeyPem is empty"`
- `Jwt__SystemSigningPrivateKey` not set in `.env`. Without it, no
  `/oauth/token` calls can succeed. Run `openssl genpkey` (see §4) and
  set the env var. Restart the api container.

### Startup failure: `"Failed to parse ... as a PEM-encoded RSA key"`
- The env var has the key with `\n` literally instead of real newlines.
  In `.env`, use `KEY="$(cat key.pem)"` (with double quotes) to preserve
  newlines, OR set the value via Docker Compose `secrets:` block instead.

### Token endpoint returns tokens that fail signature validation
- `Jwt__SystemSigningPrivateKey` and `Jwt__SystemSigningPublicKey` are
  not a matching keypair. Re-generate both from the same `openssl`
  invocation (they must come from the same `system-jwt.key`).

---

## Related code

- [src/DTMS.Api/Auth/OauthTokenEndpoint.cs](../src/DTMS.Api/Auth/OauthTokenEndpoint.cs) — token endpoint
- [src/DTMS.Api/Middlewares/SystemClientAuthMiddleware.cs](../src/DTMS.Api/Middlewares/SystemClientAuthMiddleware.cs) — inbound auth dispatch
- [src/Modules/Iam/DTMS.Iam.Application/Authorization/SystemJwtIssuer.cs](../src/Modules/Iam/DTMS.Iam.Application/Authorization/SystemJwtIssuer.cs) — token minting
- [src/Modules/Iam/DTMS.Iam.Application/Authorization/SystemJwtValidator.cs](../src/Modules/Iam/DTMS.Iam.Application/Authorization/SystemJwtValidator.cs) — token verification
- [src/Modules/Iam/DTMS.Iam.Presentation/SystemAdminEndpoints.cs](../src/Modules/Iam/DTMS.Iam.Presentation/SystemAdminEndpoints.cs) — admin CRUD + rotate
