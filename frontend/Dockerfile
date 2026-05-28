# syntax=docker/dockerfile:1.7
#
# Multi-stage build for the DTMS Templates UI. Produces a slim
# Node-based runtime image that runs the Next.js standalone server
# (no node_modules at runtime — only what @vercel/nft traced).
#
# Build from the repo root or this directory:
#   docker build -t dtms-frontend -f frontend/Dockerfile frontend/
#
# Compose builds it via the `frontend` service in ../docker-compose.yml
# under the `prod` profile:
#   docker compose --profile prod up -d
#
# Final image listens on PORT (default 3000). DTMS_BACKEND_URL is read
# at runtime by next.config.ts to wire the /api/* rewrite to the
# backend service (use http://api:8080 inside compose).

ARG NODE_VERSION=24-alpine

# ── deps stage ────────────────────────────────────────────────────────
# Installs production + dev deps. Cached layer — only re-runs when
# package.json or package-lock.json change.
FROM node:${NODE_VERSION} AS deps
WORKDIR /app

# libc6-compat is needed for some Node native modules on Alpine.
RUN apk add --no-cache libc6-compat

COPY package.json package-lock.json* ./
RUN npm ci --no-audit --no-fund

# ── builder stage ─────────────────────────────────────────────────────
# Runs `next build` with the standalone output. Telemetry is disabled
# so the image build doesn't try to phone Vercel.
FROM node:${NODE_VERSION} AS builder
WORKDIR /app

ENV NEXT_TELEMETRY_DISABLED=1

COPY --from=deps /app/node_modules ./node_modules
COPY . .

RUN npm run build

# ── runner stage ──────────────────────────────────────────────────────
# Minimal image: only the standalone bundle + public + .next/static.
# Runs as a non-root user.
FROM node:${NODE_VERSION} AS runner
WORKDIR /app

ENV NODE_ENV=production
ENV NEXT_TELEMETRY_DISABLED=1
ENV PORT=3000
ENV HOSTNAME=0.0.0.0

RUN addgroup --system --gid 1001 nodejs \
 && adduser --system --uid 1001 nextjs

# .next/standalone contains server.js + minimal node_modules.
# Static assets live outside it — copy them manually per Next docs.
COPY --from=builder --chown=nextjs:nodejs /app/.next/standalone ./
COPY --from=builder --chown=nextjs:nodejs /app/.next/static ./.next/static
COPY --from=builder --chown=nextjs:nodejs /app/public ./public

USER nextjs

EXPOSE 3000

# Lightweight healthcheck — hits the home page; treats anything < 500
# as healthy so the API not being reachable doesn't take the container
# down (the proxy rewrite happens per-request, not at startup).
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
  CMD node -e "fetch('http://localhost:3000/').then(r => process.exit(r.status < 500 ? 0 : 1)).catch(() => process.exit(1))"

CMD ["node", "server.js"]
