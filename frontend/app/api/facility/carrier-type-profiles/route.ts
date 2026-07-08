import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

// GET /api/facility/carrier-type-profiles → backend list
export async function GET() {
  return proxyToBackend({
    method: "GET",
    path: "/api/v1/facility/carrier-type-profiles",
  });
}

// POST /api/facility/carrier-type-profiles → register a carrier-type profile
export async function POST(req: NextRequest) {
  const body = await req.json().catch(() => ({}));
  return proxyToBackend({
    method: "POST",
    path: "/api/v1/facility/carrier-type-profiles",
    body,
    inbound: req,
  });
}
