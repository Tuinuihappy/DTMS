#!/usr/bin/env bash
#
# setup-system-jwt-keypair.sh — one-shot Phase 2 of the JWT-only launch
# (see docs/system-onboarding.md §1).
#
# What it does:
#   1. Generate an RSA-2048 keypair (PKCS#8 PEM) in a temp dir
#   2. Inject the PEM bodies + supporting config into the .env file
#      between marker comments (idempotent — safe to re-run; the
#      previous block is removed first)
#   3. Print a backup reminder + verification commands
#   4. Shred / remove temp keypair files
#
# Usage:
#   ./scripts/setup-system-jwt-keypair.sh                       # default: ./.env
#   ./scripts/setup-system-jwt-keypair.sh --env-file path/.env  # explicit
#   ./scripts/setup-system-jwt-keypair.sh --force               # overwrite without prompt
#   ./scripts/setup-system-jwt-keypair.sh --check-only          # just diagnose, don't touch
#   ./scripts/setup-system-jwt-keypair.sh --key-id dtms-system-prod-v2
#
# The script does NOT restart the api container — do that yourself
# (`docker compose restart api`) once you've reviewed the change.

set -euo pipefail

# ── Defaults ──────────────────────────────────────────────────────────
ENV_FILE="./.env"
FORCE=0
CHECK_ONLY=0
KEY_ID=""
BLOCK_BEGIN="# ==DTMS-JWT-SIGNING-BEGIN== (managed by scripts/setup-system-jwt-keypair.sh)"
BLOCK_END="# ==DTMS-JWT-SIGNING-END=="

# ── Args ──────────────────────────────────────────────────────────────
while [[ $# -gt 0 ]]; do
    case "$1" in
        --env-file)
            ENV_FILE="$2"; shift 2 ;;
        --force)
            FORCE=1; shift ;;
        --check-only)
            CHECK_ONLY=1; shift ;;
        --key-id)
            KEY_ID="$2"; shift 2 ;;
        -h|--help)
            # Strip the leading "#" / "# " from the header block (lines 2
            # up to but not including "set -euo pipefail").
            sed -n '2,/^set -/{ /^set -/!{ s/^# \{0,1\}//; s/^#$//; p } }' "$0"
            exit 0 ;;
        *)
            echo "Unknown arg: $1" >&2; exit 2 ;;
    esac
done

# ── Defensive checks ──────────────────────────────────────────────────
have() { command -v "$1" >/dev/null 2>&1; }

if ! have openssl; then
    echo "ERROR: openssl not found in PATH. Install OpenSSL and re-run." >&2
    exit 1
fi

if [[ ! -f "$ENV_FILE" ]]; then
    echo "ERROR: .env file not found at: $ENV_FILE" >&2
    echo "       Pass --env-file <path> or run from the directory containing .env." >&2
    exit 1
fi

if [[ ! -w "$ENV_FILE" ]]; then
    echo "ERROR: .env file is not writable: $ENV_FILE" >&2
    exit 1
fi

# ── Default KEY_ID derived from .env's parent dir for sanity ──────────
# Dev/staging/prod usually live in different compose dirs; using the
# parent dir name in the key id makes "which env signed this token?"
# obvious in jwt.io decode without an extra lookup.
if [[ -z "$KEY_ID" ]]; then
    parent_dir="$(basename "$(dirname "$(realpath "$ENV_FILE")")")"
    KEY_ID="dtms-system-${parent_dir}-v1"
fi

# ── --check-only path ────────────────────────────────────────────────
if [[ "$CHECK_ONLY" -eq 1 ]]; then
    echo "── Checking: $ENV_FILE"
    if grep -qF "$BLOCK_BEGIN" "$ENV_FILE"; then
        echo "✓ Block present (managed by this script)"
        # Pull out the key id for visibility (best effort — falls through
        # silently if format differs)
        kid=$(grep -E '^Jwt__SystemTokenKeyId=' "$ENV_FILE" | head -1 | cut -d= -f2-)
        [[ -n "$kid" ]] && echo "  KeyId: $kid"
    elif grep -qE '^Jwt__SystemSigningPrivateKey=' "$ENV_FILE"; then
        echo "⚠ Jwt__SystemSigningPrivateKey present but NOT inside the script's marker block."
        echo "  Re-running this script with --force will replace it under the markers."
    else
        echo "✗ No system signing keypair configured. Run without --check-only to set up."
    fi
    exit 0
fi

# ── Confirm overwrite if existing block present ───────────────────────
if grep -qF "$BLOCK_BEGIN" "$ENV_FILE" && [[ "$FORCE" -ne 1 ]]; then
    echo "⚠  $ENV_FILE already contains a managed JWT signing block."
    echo "   Re-running will REVOKE every currently-issued system JWT (they'll fail signature"
    echo "   validation against the new key). Partners will need to re-fetch via /oauth/token —"
    echo "   that's automatic with a 1-hour token lifetime but a brief blip during rotation."
    echo ""
    read -p "   Replace the existing keypair? [y/N] " ans
    case "$ans" in
        y|Y|yes|YES) ;;
        *) echo "Aborted."; exit 0 ;;
    esac
fi

# Same prompt for the unmanaged case (bare env var present, no markers)
# — the user might be migrating from a hand-edited setup.
if ! grep -qF "$BLOCK_BEGIN" "$ENV_FILE" \
   && grep -qE '^Jwt__SystemSigningPrivateKey=' "$ENV_FILE" \
   && [[ "$FORCE" -ne 1 ]]; then
    echo "⚠  $ENV_FILE has Jwt__SystemSigningPrivateKey set outside this script's marker block."
    echo "   Continuing will leave the existing lines in place AND add a managed block, which"
    echo "   leads to undefined precedence. Recommended: manually delete the old lines first."
    echo ""
    read -p "   Continue anyway? [y/N] " ans
    case "$ans" in
        y|Y|yes|YES) ;;
        *) echo "Aborted."; exit 0 ;;
    esac
fi

# ── Generate keypair in a temp dir ────────────────────────────────────
# Temp dir is unique per-run and lives only for this script's lifetime.
# Trap cleans up even on Ctrl-C / errors.
TMP_DIR="$(mktemp -d)"
trap 'rm -rf "$TMP_DIR" 2>/dev/null || true' EXIT INT TERM

PRIV="$TMP_DIR/system-jwt.key"
PUB="$TMP_DIR/system-jwt.pub"

echo "── Generating RSA-2048 keypair…"
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out "$PRIV" 2>/dev/null
openssl rsa -in "$PRIV" -pubout -out "$PUB" 2>/dev/null

# Sanity — fail loud if openssl produced anything unexpected
if ! head -1 "$PRIV" | grep -qE '^-----BEGIN (RSA )?PRIVATE KEY-----$'; then
    echo "ERROR: Generated private key doesn't look like PEM. Check openssl version." >&2
    exit 1
fi
if ! head -1 "$PUB" | grep -qE '^-----BEGIN (RSA )?PUBLIC KEY-----$'; then
    echo "ERROR: Generated public key doesn't look like PEM." >&2
    exit 1
fi

# ── Build the new .env block in a temp file ───────────────────────────
NEW_BLOCK="$TMP_DIR/block.env"
{
    echo "$BLOCK_BEGIN"
    echo "# Generated $(date -u +%Y-%m-%dT%H:%M:%SZ) — back up the PRIVATE key now."
    echo "# Losing it means re-issuing client_secret to every partner."
    echo "Jwt__SystemSigningPrivateKey=\"$(cat "$PRIV")\""
    echo "Jwt__SystemSigningPublicKey=\"$(cat "$PUB")\""
    echo "Jwt__SystemTokenKeyId=$KEY_ID"
    echo "Jwt__SystemTokenIssuer=dtms"
    echo "Jwt__SystemTokenAudience=dtms-api"
    echo "Jwt__SystemTokenLifetimeSeconds=3600"
    echo "$BLOCK_END"
} > "$NEW_BLOCK"

# ── Rewrite .env: strip existing block, append new one ────────────────
# awk pass: skip lines between BEGIN and END markers (inclusive), keep
# everything else verbatim. Then append the freshly-built block.
REWRITTEN="$TMP_DIR/env.new"
awk -v begin="$BLOCK_BEGIN" -v end="$BLOCK_END" '
    $0 == begin { in_block = 1; next }
    $0 == end   { in_block = 0; next }
    !in_block   { print }
' "$ENV_FILE" > "$REWRITTEN"

# Ensure trailing newline so the new block doesn't fuse onto the last line
[[ -s "$REWRITTEN" ]] && [[ "$(tail -c1 "$REWRITTEN")" != "" ]] && echo "" >> "$REWRITTEN"
echo "" >> "$REWRITTEN"
cat "$NEW_BLOCK" >> "$REWRITTEN"

# Backup the original .env (in case of operator regret); name includes
# a timestamp so successive runs don't overwrite each other.
BACKUP="${ENV_FILE}.bak.$(date -u +%Y%m%dT%H%M%SZ)"
cp "$ENV_FILE" "$BACKUP"
mv "$REWRITTEN" "$ENV_FILE"

# ── Best-effort secure delete of the temp keypair ─────────────────────
# `shred` doesn't exist on macOS/Git-Bash-on-Windows, so try it but fall
# back to plain rm. The temp dir is auto-removed by the EXIT trap anyway.
if have shred; then
    shred -u "$PRIV" "$PUB" 2>/dev/null || rm -f "$PRIV" "$PUB"
else
    rm -f "$PRIV" "$PUB"
fi

# ── Summary ───────────────────────────────────────────────────────────
cat <<EOF

✓ Done.

Wrote managed JWT signing block to: $ENV_FILE
Backup of previous .env saved to:   $BACKUP
Key ID stamped into tokens:         $KEY_ID
Token lifetime:                     3600s (1 hour)

NEXT STEPS

  1. BACKUP the keypair NOW (private + public) into your password vault
     or secret manager. The script generated it in a temp dir that's
     already wiped — the only remaining copy is inside $ENV_FILE.

     Quick extract:
       sed -n '/^Jwt__SystemSigningPrivateKey="/,/-----END.*KEY-----"/p' $ENV_FILE \\
         | sed 's/^Jwt__SystemSigningPrivateKey="//; s/"$//'

  2. Restart the api service so it loads the new keypair:
       docker compose restart api

  3. Smoke test that the keypair loaded (NOT a real partner; expects 401):
       curl -X POST http://localhost:8080/oauth/token \\
         -H "Content-Type: application/x-www-form-urlencoded" \\
         -d "grant_type=client_credentials&client_id=__notreal__&client_secret=x"
     Expected: {"error":"invalid_client",...}
     If you get 500 instead, check api logs — usually a PEM parse error.

  4. Onboard the first real partner via admin UI: see docs/system-onboarding.md §2.

ROTATION
  Re-run this script (with --force to skip the confirmation) when you
  need to rotate the keypair. All in-flight tokens fail signature
  validation immediately; partners auto-recover by re-fetching via
  /oauth/token within their token-cache refresh interval.

EOF
