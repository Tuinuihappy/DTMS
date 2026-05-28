import type { NextConfig } from "next";

// Dev/local proxy: forward every /api/* request to the DTMS backend so the
// browser sees a same-origin call (no CORS dance required on the backend).
// In staging/prod, swap this for a real reverse proxy / Ingress route.
const backendUrl =
  process.env.DTMS_BACKEND_URL ?? "http://localhost:5219";

const nextConfig: NextConfig = {
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
