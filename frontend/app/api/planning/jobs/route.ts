import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

// GET /api/planning/jobs?orderId={guid} — list Jobs for an order.
// Proxies to backend GET /api/v1/planning/jobs?orderId=...
export async function GET(req: NextRequest) {
  const orderId = req.nextUrl.searchParams.get("orderId");
  const search = orderId ? new URLSearchParams({ orderId }) : undefined;
  return proxyToBackend({
    method: "GET",
    path: "/api/v1/planning/jobs",
    search,
    inbound: req,
  });
}
