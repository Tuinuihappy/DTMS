import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

// The backend route moved to Fleet in carrier-tracking P0.2 (ADR-019). This
// Next route keeps its own path for now so the page above it is untouched;
// it moves to app/api/fleet/carrier-types in P1.2 along with the page.

// GET /api/facility/carrier-type-profiles → backend list
export async function GET() {
  return proxyToBackend({
    method: "GET",
    path: "/api/v1/fleet/carrier-types",
  });
}

// POST /api/facility/carrier-type-profiles → register a carrier type
export async function POST(req: NextRequest) {
  const body = await req.json().catch(() => ({}));
  return proxyToBackend({
    method: "POST",
    path: "/api/v1/fleet/carrier-types",
    body,
    inbound: req,
  });
}
