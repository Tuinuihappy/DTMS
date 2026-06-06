import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

type Ctx = { params: Promise<{ id: string }> };

export async function POST(req: NextRequest, { params }: Ctx) {
  const { id } = await params;
  let body: unknown = {};
  try {
    body = await req.json();
  } catch {
    // Backend rejects missing RedispatchedBy / Reason with a clear error.
  }
  return proxyToBackend({
    method: "POST",
    path: `/api/v1/delivery-orders/${id}/redispatch`,
    body,
    inbound: req,
  });
}
