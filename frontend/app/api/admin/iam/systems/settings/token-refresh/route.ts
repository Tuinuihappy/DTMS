import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

// Read-only: platform-managed auto-refresh settings (SSRF allowlist, cadence).
export async function GET(req: NextRequest) {
  return proxyToBackend({
    method: "GET",
    path: "/api/v1/iam/systems/settings/token-refresh",
    inbound: req,
  });
}
