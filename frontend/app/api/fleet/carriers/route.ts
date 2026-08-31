import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

// GET /api/fleet/carriers?status=&carrierTypeCode=&q=&page=&pageSize=
export async function GET(req: NextRequest) {
  return proxyToBackend({
    method: "GET",
    path: "/api/v1/fleet/carriers",
    search: req.nextUrl.searchParams,
  });
}

// POST /api/fleet/carriers → register a carrier
export async function POST(req: NextRequest) {
  const body = await req.json().catch(() => ({}));
  return proxyToBackend({
    method: "POST",
    path: "/api/v1/fleet/carriers",
    body,
    inbound: req,
  });
}
