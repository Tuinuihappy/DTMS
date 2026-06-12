import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

type Ctx = { params: Promise<{ id: string }> };

export async function POST(req: NextRequest, { params }: Ctx) {
  const { id } = await params;
  let body: unknown = {};
  try {
    body = await req.json();
  } catch {
    // body required by backend — fall through; backend rejects with a
    // clear "AbandonedBy is required" message.
  }
  return proxyToBackend({
    method: "POST",
    path: `/api/v1/delivery-orders/${id}/abandon-after-trip-cancel`,
    body,
    inbound: req,
  });
}
