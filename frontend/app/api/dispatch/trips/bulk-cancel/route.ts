import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

// POST /api/dispatch/trips/bulk-cancel → backend bulk trip cancel
// Body: { tripIds: string[], reason: string }. Returns 200/207 (partial).
export async function POST(req: NextRequest) {
  const body = await req.json().catch(() => ({}));
  return proxyToBackend({
    method: "POST",
    path: "/api/v1/dispatch/trips/bulk-cancel",
    body,
    inbound: req,
  });
}
