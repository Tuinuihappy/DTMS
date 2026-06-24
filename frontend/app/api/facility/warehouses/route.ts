import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

// Phase 2.7b — list + create. Mirrors the stations proxy pattern;
// the only difference is the upstream path which is at the versioned
// /api/v1/facility/warehouses (per Phase 2.7a).

export async function GET(req: NextRequest) {
  const search = new URL(req.url).searchParams;
  return proxyToBackend({
    method: "GET",
    path: "/api/v1/facility/warehouses",
    search,
  });
}

export async function POST(req: NextRequest) {
  const body = await req.json().catch(() => ({}));
  return proxyToBackend({
    method: "POST",
    path: "/api/v1/facility/warehouses",
    body,
    inbound: req,
  });
}
