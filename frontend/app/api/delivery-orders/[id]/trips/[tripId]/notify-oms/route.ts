import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

type Ctx = { params: Promise<{ id: string; tripId: string }> };

export async function POST(req: NextRequest, { params }: Ctx) {
  const { id, tripId } = await params;
  let body: unknown = {};
  try {
    body = await req.json();
  } catch {
    // Body is optional — backend accepts empty payload (RequestedBy defaults to null).
  }
  return proxyToBackend({
    method: "POST",
    path: `/api/v1/delivery-orders/${id}/trips/${tripId}/notify-oms`,
    body,
    inbound: req,
  });
}
