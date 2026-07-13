# Source-System Onboarding Runbook

DTMS authenticates federated source systems (OMS, SAP, WMS adapters,
etc.) with **OAuth 2.0 client_credentials** (RFC 6749 §4.4). Each system
holds a long-lived `client_secret`, exchanges it for a short-lived JWT
(1 hour default) via `POST /oauth/token`, then sends the JWT as
`Authorization: Bearer <token>` on inbound API calls.

For partners that cannot run an OAuth client, an admin can also **issue a
JWT directly** — bounded (up to 365 days) or perpetual — that the partner
pastes in as a fixed `Bearer` token (§2.5). Both paths produce the same
RS256 JWT validated the same way.

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
| 5 | Confirm at least one External Auth user has the **`Admin`** role | Admin | that user can open **Admin → IAM** |
| 6 | Onboard first partner (e.g. OMS) — see §2 | Admin | partner integration test passes |
| 7 | Set monitoring on 401 rate of `/api/v1/source/*` + token endpoint | DevOps | dashboards live |

If step 4 returns 500 → keypair PEM is malformed or env var not set.
See [troubleshooting §5](#5-troubleshooting).

> **A fresh database is empty by design.** Migrations seed **only** the
> `Admin` role granted the `dtms:*` wildcard — nothing else. There are no
> pre-created SystemClients, no partner credentials, and no permission
> catalog; every system is onboarded by hand (§2). The first operator gets
> in because **External Auth assigns them the `Admin` role**, which the
> bootstrap maps to `dtms:*`; from there they can create systems, grant
> permissions, and issue tokens. **If no External Auth user holds the
> `Admin` role, nobody can configure anything** — line this up before
> launch (step 5). Point this migration set only at a *fresh* database:
> the seed-reset migration wipes the IAM tables, so running it against an
> environment that already holds real partner data would delete it.

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
   `/api/v1/source/whoami`) and reports back whether auth works end-to-end.
   (Identity comes from the JWT `sub` claim; the URL carries no `{key}`
   segment.)
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
3. Pick a lifetime (presets: 7 / 30 / 90 / 180 / 365 days; default 90), or
   tick **☑ Never expires** for a perpetual token (see the warning below)
4. Click **Issue token** — banner shows the JWT plaintext (`eyJ...`)
5. Click **Test this credential** — confirms the JWT authenticates
   against `/api/v1/source/whoami` directly (no OAuth round-trip)
6. Send the JWT to the partner via a secure channel
7. Partner uses it directly:

```bash
curl -X POST https://dtms.internal/api/v1/source/delivery-orders \
  -H "Authorization: Bearer eyJhbGciOiJSUzI1NiIs..." \
  -H "Content-Type: application/json" \
  -d '{...order payload...}'
```

### ⚠️ Perpetual (never-expires) tokens (Phase S.8d)

Ticking **Never expires** mints a JWT with **no `exp` claim** — it is
accepted forever until you revoke it. Use it only for a partner that
genuinely cannot re-fetch a token and must paste one fixed value into a
config. Prefer a bounded lifetime (or the OAuth flow) whenever possible.

- **Only kill switch is Revoke** (see below) — there is no natural expiry
  to bound the blast radius if the token leaks. Treat it as a standing
  credential: store it in a secret manager, restrict who can see it, and
  revoke the instant you suspect exposure.
- **Backed by a durable allowlist.** A perpetual token is valid only while
  its row in `iam.SystemIssuedTokens` is `Active`. Revoke flips that row to
  `Revoked` (durable) *and* writes the Redis blocklist (instant), so a
  Redis flush cannot resurrect a revoked forever-token.
- **Redis persistence matters.** The revoke entry for a perpetual token is
  written with **no TTL**. Run Redis with AOF + a non-`allkeys` eviction
  policy (the shipped compose uses `--appendonly yes` +
  `--maxmemory-policy volatile-lru`) so the blocklist survives restarts and
  is never evicted. The DB allowlist is the backstop either way.

### Trade-offs you accept

| Risk | Mitigation |
|---|---|
| JWT leaked → usable until it expires (up to 365 days, or **forever** for a perpetual token) | Pick the shortest lifetime your partner can cope with. **Revoke** the specific token (per-jti, see below) — this is the only kill switch for a perpetual one. Deactivating the system kills all its tokens; rotating the signing keypair (§4.3) revokes globally |
| Admin needs to re-issue + re-distribute before a bounded token expires | Calendar the expiry; the Issued-tokens list flags any token due within 7 days |

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
3. **Next request** with that token → validator sees the Redis entry (and,
   for perpetual tokens, the `Revoked` DB row) → middleware returns
   401 `"jwt invalid"`.
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
5. Tokens minted by the old keypair fail validation immediately (no overlap
   window) — OAuth partners re-fetch via `/oauth/token` on their next call
   and automatically get new-keyed tokens.
6. Worst case for OAuth partners: in-flight requests during the restart
   return 401 once → retry succeeds. Typically zero user impact.

> ⚠️ **Admin-issued and perpetual tokens do NOT self-recover.** They are
> pasted-in fixed strings with no `/oauth/token` refresh, so rotating the
> keypair permanently breaks them. After a rotation you must **re-issue and
> re-distribute** every admin-issued/perpetual token. Keep a list (the
> Issued-tokens view per system) and coordinate before rotating.

The public half is published at **`GET /.well-known/jwks.json`** (JWKS,
RFC 7517) so a downstream gateway or partner can verify DTMS-issued tokens
without holding the raw PEM. A multi-key **overlap window** (old keys stay
valid until their tokens expire) is still deferred — for now rotation is a
clean replace-and-restart, which is fine because OAuth token lifetime is
only 1 hour (but see the admin-issued caveat above).

---

## 5. Troubleshooting

### `401 invalid_client` from `/oauth/token`
- Partner sending wrong `client_secret` → verify in DTMS admin UI; rotate if necessary
- Partner's `client_id` doesn't match a SystemClient → check `key` spelling
- System is deactivated → reactivate before token requests will succeed

### `401 jwt invalid` from `/api/v1/source/*`
- Token expired → partner's cache is stale; force a re-fetch
- Token signature invalid → DTMS keypair rotated since token was minted;
  partner needs to re-fetch (will use new key)
- Token was **revoked** (per-jti Revoke, or its system Deactivated/Deleted)
  → issue a fresh one
- Perpetual token rejected as `"token not active"` in logs → its
  `SystemIssuedTokens` row is missing or `Revoked` → re-issue
- Identity comes from the JWT `sub` (not the URL). Each system has its own
  client_secret + token — never share tokens across systems

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
- [src/DTMS.Api/Auth/JwksEndpoint.cs](../src/DTMS.Api/Auth/JwksEndpoint.cs) — `/.well-known/jwks.json` public-key publication
- [src/DTMS.Api/Middlewares/SystemClientAuthMiddleware.cs](../src/DTMS.Api/Middlewares/SystemClientAuthMiddleware.cs) — inbound auth dispatch
- [src/Modules/Iam/DTMS.Iam.Application/Authorization/SystemJwtIssuer.cs](../src/Modules/Iam/DTMS.Iam.Application/Authorization/SystemJwtIssuer.cs) — token minting
- [src/Modules/Iam/DTMS.Iam.Application/Authorization/SystemJwtValidator.cs](../src/Modules/Iam/DTMS.Iam.Application/Authorization/SystemJwtValidator.cs) — token verification
- [src/Modules/Iam/DTMS.Iam.Presentation/SystemAdminEndpoints.cs](../src/Modules/Iam/DTMS.Iam.Presentation/SystemAdminEndpoints.cs) — admin CRUD + rotate
