import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

// GET /api/facility/route-cost?from=&to= → backend route cost/distance
export async function GET(req: NextRequest) {
  const search = new URL(req.url).searchParams;
  return proxyToBackend({
    method: "GET",
    path: "/api/v1/facility/route-cost",
    search,
    // Pass 404 (no edge) through so the client can show a specific message.
    passthroughErrors: true,
  });
}
