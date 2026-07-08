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
