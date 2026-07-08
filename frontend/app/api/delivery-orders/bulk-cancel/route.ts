import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

// POST /api/delivery-orders/bulk-cancel → backend bulk order cancel
// Body: { orderIds: string[], reason: string }. Requires an Idempotency-Key
// (forwarded via `inbound`). Returns 200 (all) / 207 (partial).
export async function POST(req: NextRequest) {
  const body = await req.json().catch(() => ({}));
  return proxyToBackend({
    method: "POST",
    path: "/api/v1/delivery-orders/bulk-cancel",
    body,
    inbound: req,
  });
}
