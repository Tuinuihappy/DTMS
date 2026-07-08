import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

// GET /api/facility/load-unit-profiles?carrierTypeCode= → backend list
export async function GET(req: NextRequest) {
  const search = new URL(req.url).searchParams;
  return proxyToBackend({
    method: "GET",
    path: "/api/v1/facility/load-unit-profiles",
    search,
  });
}

// POST /api/facility/load-unit-profiles → register a load-unit profile
export async function POST(req: NextRequest) {
  const body = await req.json().catch(() => ({}));
  return proxyToBackend({
    method: "POST",
    path: "/api/v1/facility/load-unit-profiles",
    body,
    inbound: req,
  });
}
