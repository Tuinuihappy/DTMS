import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

// GET /api/planning/jobs/queue?statuses=Failed&statuses=Created&page=1&pageSize=20
// Proxies to backend GET /api/v1/planning/jobs/queue with the same query
// string verbatim — backend handles status-set parsing + validation.
export async function GET(req: NextRequest) {
  return proxyToBackend({
    method: "GET",
    path: "/api/v1/planning/jobs/queue",
    search: req.nextUrl.searchParams,
    inbound: req,
  });
}
