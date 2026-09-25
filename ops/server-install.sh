#!/usr/bin/env bash
# ---------------------------------------------------------------------------
#  Installs a bundle made by ops/server-export.ps1 onto a Linux server that
#  runs Docker Engine and has no internet. Run it from inside the bundle:
#
#    sudo bash server-install.sh              # the real move (-Cutover bundle)
#    sudo bash server-install.sh --rehearsal  # trial run while the old stack is live
#
#  --rehearsal starts the stack with RIOT3, WMS, the outbox and the callback
#  token refresh switched off, so a copy of production can be clicked through
#  without it sending anything to RIOT3, OMS or WMS alongside the old machine.
#
#  Fresh servers only: it refuses to run if any dtms_* volume already exists,
#  rather than restore over data it has not looked at.
#
#  Afterwards, day-to-day compose commands go through ./compose.sh, which
#  carries the right -f files, e.g. ./compose.sh ps, ./compose.sh logs -f api
# ---------------------------------------------------------------------------
set -euo pipefail

REHEARSAL=0
for arg in "$@"; do
  case "$arg" in
    --rehearsal) REHEARSAL=1 ;;
    *) echo "unknown argument: $arg" >&2; exit 2 ;;
  esac
done

cd "$(dirname "$0")"
BUNDLE="$(pwd)"

step() { printf '\n\033[36m==> %s\033[0m\n' "$*"; }
die()  { printf '\033[31mERROR: %s\033[0m\n' "$*" >&2; exit 1; }

# shellcheck disable=SC1091
source ./bundle.env   # SERVER_HOST, CUTOVER, CREATED

if [ "$CUTOVER" != "1" ] && [ "$REHEARSAL" != "1" ]; then
  die "this bundle was exported without -Cutover, so the old machine is still live.
Starting a full copy here would drive RIOT3 and OMS twice. Either re-export with
-Cutover, or install it as a trial with: sudo bash server-install.sh --rehearsal"
fi

VOLUMES=(pgdata dp-keys miniodata redis-data grafana-data rabbitmqdata prometheus-data)
IMPORTED=(dp-keys miniodata redis-data grafana-data)

step 'checking docker'
command -v docker >/dev/null || die 'docker is not installed.'
docker info >/dev/null 2>&1 || die 'cannot talk to the docker daemon (run with sudo, or start it: systemctl start docker).'
docker compose version >/dev/null 2>&1 || die 'the docker compose plugin is missing (package docker-compose-plugin).'
# docker-compose.server.yml uses !override, which compose understands from 2.24.4.
CV="$(docker compose version --short | sed 's/^v//')"
if [ "$(printf '%s\n2.24.4\n' "$CV" | sort -V | head -n1)" != "2.24.4" ]; then
  die "docker compose $CV is too old; 2.24.4 or newer is needed."
fi
[ "$(uname -m)" = "x86_64" ] || die "the images are amd64; this server is $(uname -m)."

for v in "${VOLUMES[@]}"; do
  if docker volume inspect "dtms_$v" >/dev/null 2>&1; then
    die "volume dtms_$v already exists - this script only installs onto a fresh server.
Look at what is in it before removing anything. If it is only left over from a
failed run of this script, clear it with: ./compose.sh down -v"
  fi
done

step 'verifying the bundle (SHA256SUMS)'
# Only the payload: .env is edited below, so checking it would fail a re-run.
grep -E '  (images|db|volumes)/' SHA256SUMS | sha256sum --quiet -c - ||
  die 'bundle is damaged or incomplete - copy it again.'

step 'loading images (takes a few minutes)'
docker load -i images/dtms-images.tar
# podman names locally-built images localhost/..., compose looks for the bare name.
docker tag localhost/dtms-api:latest dtms-api:latest
docker tag localhost/tms-frontend:server tms-frontend:latest

step 'preparing .env'
sed -i 's/\r$//' .env
FRONTEND_ORIGIN="http://${SERVER_HOST}:3000"
if grep -q '^Cors__HubsAllowedOrigins=' .env; then
  grep -q "^Cors__HubsAllowedOrigins=.*${FRONTEND_ORIGIN}\(,\|$\)" .env ||
    sed -i "s|^Cors__HubsAllowedOrigins=.*|&,${FRONTEND_ORIGIN}|" .env
else
  echo "Cors__HubsAllowedOrigins=http://localhost:3000,${FRONTEND_ORIGIN}" >> .env
fi
# Already baked into the frontend image; kept here so a rebuild on this server
# would bake the same value.
grep -q '^NEXT_PUBLIC_DTMS_HUBS_URL=' .env ||
  echo "NEXT_PUBLIC_DTMS_HUBS_URL=http://${SERVER_HOST}:5219" >> .env

FILES=(-f docker-compose.yml -f docker-compose.server.yml)
if [ "$REHEARSAL" = "1" ]; then
  cat > docker-compose.rehearsal.yml <<'EOF'
# Written by server-install.sh --rehearsal: nothing here talks to RIOT3, OMS
# or WMS while the old machine is still the live one. Delete this file and
# `./compose.sh up -d` after the real cutover (or reinstall from a -Cutover bundle).
services:
  api:
    environment:
      - VendorAdapter__Riot3__Enabled=false
      - Wms__Enabled=false
      - Workers__VendorPollers__RunInThisProcess=false
      - Outbox__RunInThisProcess=false
  outbox-worker:
    environment:
      - VendorAdapter__Riot3__Enabled=false
      - Wms__Enabled=false
      - Outbox__RunInThisProcess=false
      - Workers__CallbackTokenRefresh__RunInThisProcess=false
EOF
  FILES+=(-f docker-compose.rehearsal.yml)
fi

{
  echo '#!/usr/bin/env bash'
  echo '# Runs docker compose with the files this install uses.'
  echo 'cd "$(dirname "$0")"'
  printf 'exec docker compose'; printf ' %q' "${FILES[@]}"; echo ' --profile prod "$@"'
} > compose.sh
chmod +x compose.sh

step 'creating volumes'
for v in "${VOLUMES[@]}"; do
  # The compose labels stop compose warning that it did not create them.
  docker volume create \
    --label com.docker.compose.project=dtms \
    --label "com.docker.compose.volume=$v" \
    "dtms_$v" >/dev/null
done
for v in "${IMPORTED[@]}"; do
  echo "  importing $v"
  docker run --rm -v "dtms_$v:/v" -v "$BUNDLE/volumes:/b:ro" alpine:3.20 \
    tar -xpf "/b/$v.tar" -C /v
done

wait_healthy() {  # container, seconds
  local name="$1" deadline=$((SECONDS + $2)) s
  while [ $SECONDS -lt $deadline ]; do
    s="$(docker inspect -f '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' "$name" 2>/dev/null || true)"
    [ "$s" = "healthy" ] && return 0
    sleep 3
  done
  echo "  $name: ${s:-missing}"
  return 1
}

step 'restoring the database'
./compose.sh up -d --no-build postgres
wait_healthy dtms-postgres 120 || die 'postgres did not become healthy: ./compose.sh logs postgres'
docker cp db/dtms.dump dtms-postgres:/tmp/dtms.dump
# One transaction: a failed restore leaves an empty database, never half of one.
docker exec dtms-postgres pg_restore -U postgres -d amr_delivery_planning \
  --no-owner --exit-on-error --single-transaction /tmp/dtms.dump
docker exec dtms-postgres rm -f /tmp/dtms.dump

step 'starting the stack'
./compose.sh up -d --no-build

step 'waiting for health (up to 5 min)'
ok=1
for c in dtms-api dtms-outbox-worker tms-frontend; do
  if wait_healthy "$c" 300; then echo "  $c healthy"; else ok=0; fi
done

if command -v systemctl >/dev/null; then
  # restart: unless-stopped brings the containers back only if dockerd does.
  systemctl enable docker >/dev/null 2>&1 || true
fi

[ "$ok" = "1" ] || die 'installed, but not everything is healthy. Start with: ./compose.sh ps && ./compose.sh logs --tail 100 api'

step 'done'
cat <<EOF
  Web:      http://${SERVER_HOST}:3000
  API:      http://${SERVER_HOST}:5219/health/ready
  Grafana:  http://${SERVER_HOST}:3001
  Infra ports (postgres 5434, rabbitmq, redis, minio, jaeger, prometheus)
  listen on 127.0.0.1 only; use an SSH tunnel to reach them.
EOF
if [ "$REHEARSAL" = "1" ]; then
  echo
  echo "  REHEARSAL: RIOT3, WMS, outbox and token refresh are OFF (docker-compose.rehearsal.yml)."
fi
