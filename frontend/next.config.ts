import type { NextConfig } from "next";

// Dev/local proxy: forward every /api/* request to the DTMS backend so the
// browser sees a same-origin call (no CORS dance required on the backend).
// In staging/prod, swap this for a real reverse proxy / Ingress route.
const backendUrl =
  process.env.DTMS_BACKEND_URL ?? "http://localhost:5219";

const nextConfig: NextConfig = {
  // `standalone` emits .next/standalone/server.js — a minimal runner that
  // doesn't need node_modules at deploy time. The Dockerfile copies that
  // folder into a slim runtime image (see ../Dockerfile-style multi-stage
  // in frontend/Dockerfile). Local `npm run dev` ignores this setting.
  output: "standalone",

  async rewrites() {
    return [
      {
        source: "/api/:path*",
        destination: `${backendUrl}/api/:path*`,
      },
    ];
  },
};

export default nextConfig;
