import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

// GET /api/fleet/carrier-types → backend list
export async function GET() {
  return proxyToBackend({
    method: "GET",
    path: "/api/v1/fleet/carrier-types",
  });
}

// POST /api/fleet/carrier-types → register a carrier type
export async function POST(req: NextRequest) {
  const body = await req.json().catch(() => ({}));
  return proxyToBackend({
    method: "POST",
    path: "/api/v1/fleet/carrier-types",
    body,
    inbound: req,
  });
}
