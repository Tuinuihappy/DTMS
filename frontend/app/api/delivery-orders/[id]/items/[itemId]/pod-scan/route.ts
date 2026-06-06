import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

type Ctx = { params: Promise<{ id: string; itemId: string }> };

export async function POST(req: NextRequest, { params }: Ctx) {
  const { id, itemId } = await params;
  let body: unknown = {};
  try {
    body = await req.json();
  } catch {
    // Backend returns a clear "ScannedBy is required" / "Method..." error.
  }
  return proxyToBackend({
    method: "POST",
    path: `/api/v1/delivery-orders/${id}/items/${itemId}/pod-scan`,
    body,
    inbound: req,
  });
}
