# Run DTMS on Docker — Quickstart

Production-shape deployment via `docker-compose.yml` at the repo root.
This guide covers first-time bring-up after the JWT-only launch.

For partner onboarding details (post-startup), see
[system-onboarding.md](system-onboarding.md).

---

## Prerequisites

| Tool | Why |
|---|---|
| Docker Engine + Compose v2 | Stack runtime |
| OpenSSL | Generate JWT signing keypair |
| Bash (Git Bash on Windows is fine) | Run `scripts/setup-system-jwt-keypair.sh` |
| `~10 GB free disk` | Postgres + RabbitMQ + Redis + MinIO + Jaeger volumes |

---

## 4-step bring-up (first time on a new host)

### 1. Set required env vars in `.env`

The repo's `.env` is gitignored — copy from `.env.example` and fill in
secrets that **only your team can supply**:

```bash
cp .env.example .env
```

Edit `.env` and set at minimum:

| Variable | Where to get it |
|---|---|
| `Jwt__PublicKey` | External Auth team (PKCS#1 base64 RSA public key) |
| `UpstreamOms__BearerToken` | OMS ops (long-lived JWT used for outbound callbacks) |
| `VendorAdapter__Riot3__ApiKey` | RIOT3 ops (vendor API key) |

If you skip any of these, `docker compose up` aborts with a clear
"required variable X is missing" error — no silent half-starts.

### 2. Generate the system JWT signing keypair

DTMS signs OAuth tokens for source-system callers (OMS, etc.) with its
own RSA keypair, separate from External Auth above. Run the helper
script — it generates the keypair, injects the PEM bodies into `.env`
between marker comments, and shreds the temp files:

```bash
./scripts/setup-system-jwt-keypair.sh
```

The script is idempotent (re-run any time for rotation). Use
`--check-only` to diagnose without writing, `--force` to skip prompts.

**BACK UP THE KEYPAIR** into your password vault immediately after this
step — losing the private key means re-issuing `client_secret` to every
partner.

### 3. Start the stack

```bash
docker compose up -d
```

This brings up: postgres + pgbouncer + migrator + rabbitmq + redis +
minio + jaeger + api + outbox-worker.

Frontend is opt-in via the `prod` profile (UI devs usually run
`npm run dev` from `frontend/` directly during development):

```bash
docker compose --profile prod up -d   # api + frontend together
```

First boot takes 60-90 seconds (waiting on postgres health + EF
migrations + .NET JIT). Watch progress:

```bash
docker compose logs -f api
```

### 4. Verify

| Check | Command | Expected |
|---|---|---|
| API health | `curl http://localhost:5219/health/ready` | `Healthy` |
| Keypair loaded | `curl -X POST http://localhost:5219/oauth/token -H "Content-Type: application/x-www-form-urlencoded" -d "grant_type=client_credentials&client_id=__x__&client_secret=x"` | `{"error":"invalid_client",...}` (NOT 500) |
| Swagger | open `http://localhost:5219/scalar/v1` | OAuth tag visible, `/oauth/token` endpoint shows form schema |
| Admin UI | open `http://localhost:3000/admin/systems` | empty list (no systems yet — proceed to onboarding) |

If `/oauth/token` returns **500** instead of `invalid_client`, the
keypair didn't load. Check `docker compose logs api | grep -i jwt` —
usually a malformed PEM or missing newlines in `.env`.

---

## First-time partner onboarding (OMS)

After step 4 above passes, onboard OMS as the first source-system caller:

1. Login admin UI → **Admin → IAM → Systems → New system**
2. Key: `oms`, DisplayName: "OMS Production", owner contact
3. Click **Create system** — the banner shows the `client_secret` plaintext
4. **Copy the secret** before dismissing — it's unrecoverable after
5. Click **Test this credential** → expect `ok: true` with permissions list
6. Send the secret to the OMS team via a secure channel (1Password vault,
   not Slack/email)

OMS team then updates their bot to fetch tokens via
`POST /oauth/token` — full integration walkthrough in
[system-onboarding.md §2.3](system-onboarding.md#23-partner-integration).

---

## Daily operations

```bash
# Start everything
docker compose up -d

# Stop everything (keeps volumes — DB data preserved)
docker compose down

# Stop + delete data volumes (full reset — RARE)
docker compose down -v

# Tail api logs (most active)
docker compose logs -f api

# Restart api only (after .env change)
docker compose restart api

# Rebuild api image (after C# code change)
docker compose build api && docker compose up -d api

# Status overview
docker compose ps
```

### After editing `.env`
Container env vars are baked at `docker compose up` time — a plain
`restart` re-reads them on most setups, but to be safe after changing
any `Jwt__*` value or other auth-relevant config:

```bash
docker compose up -d api outbox-worker   # re-creates containers with new envs
```

### After editing C# code
```bash
docker compose build api
docker compose up -d api outbox-worker
```
(`outbox-worker` reuses the api image, so rebuilding api updates both.)

### Rotate the system JWT keypair (every 6-12 months)
```bash
./scripts/setup-system-jwt-keypair.sh --force --key-id dtms-system-prod-v2
docker compose restart api outbox-worker
```
All in-flight tokens fail signature validation immediately. Partners
auto-recover on their next `/oauth/token` call — typical impact:
0 user-visible failures, partner bot logs one 401 retry.

---

## Common issues

### `error while interpolating ... Jwt__SystemSigningPrivateKey is missing`
Run `./scripts/setup-system-jwt-keypair.sh` first, then retry
`docker compose up -d`.

### `Jwt__PublicKey must be set in .env from External Auth team`
The External Auth public key is required — DTMS validates user JWTs
against it. Get from auth ops, paste into `.env`, restart.

### API container stuck at "starting" or restart-looping
```bash
docker compose logs api | tail -50
```
Most common causes:
- Postgres not ready yet (wait 60-90s on cold start; `migrator` runs first)
- Malformed PEM in `Jwt__SystemSigningPrivateKey` (regenerate via script)
- Port 5219 already in use on host (change `ports:` in compose, or stop
  conflicting service)

### `Test this credential` button fails with "Couldn't reach the backend"
The Next.js admin route handler proxies to `DTMS_BACKEND_URL`. Verify
the env var on the frontend container or your `npm run dev` shell points
at `http://localhost:5219` (api host port) — NOT `http://api:8080`
(that's the container-internal address; only valid inside the Docker
network).

### Partner reports `401 invalid_client` from `/oauth/token`
Either the `client_secret` is wrong or the SystemClient is deactivated.
Reproduce by clicking **Test this credential** in the admin UI; if it
fails there too, rotate the secret and send the new value.

---

## What's running on which port

| Service | Host port | Container port | Purpose |
|---|---|---|---|
| api | 5219 | 8080 | REST + SignalR + /oauth/token |
| frontend (`prod` profile) | 3000 | 3000 | Next.js admin/operator UI |
| postgres | 5434 | 5432 | DB (`amr_delivery_planning`) |
| pgbouncer | 6432 | 5432 | Connection pool (api connects here) |
| rabbitmq | 5672 + 15672 | 5672 + 15672 | AMQP + management UI |
| redis | 6379 | 6379 | Cache + SignalR backplane |
| minio | 9000 + 9001 | 9000 + 9001 | POD photo storage + console |
| jaeger | 16686 | 16686 | Trace UI |

Internal container DNS names (used in inter-service config): `postgres`,
`pgbouncer`, `rabbitmq`, `redis`, `minio`, `api`, `jaeger`.

---

## Related docs

- [system-onboarding.md](system-onboarding.md) — partner onboarding, keypair rotation, troubleshooting auth flows
- [scripts/setup-system-jwt-keypair.sh](../scripts/setup-system-jwt-keypair.sh) — one-shot keypair setup; run `--help` for options
- `.env.example` — full list of supported env vars with inline docs
