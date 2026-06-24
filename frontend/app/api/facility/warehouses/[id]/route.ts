import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

// GET full detail + PATCH partial update for a single warehouse.

export async function GET(
  _req: NextRequest,
  { params }: { params: Promise<{ id: string }> },
) {
  const { id } = await params;
  return proxyToBackend({
    method: "GET",
    path: `/api/v1/facility/warehouses/${id}`,
  });
}

export async function PATCH(
  req: NextRequest,
  { params }: { params: Promise<{ id: string }> },
) {
  const { id } = await params;
  const body = await req.json().catch(() => ({}));
  return proxyToBackend({
    method: "PATCH",
    path: `/api/v1/facility/warehouses/${id}`,
    body,
    inbound: req,
  });
}
