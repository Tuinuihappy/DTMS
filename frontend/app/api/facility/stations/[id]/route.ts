import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

export async function PATCH(
  req: NextRequest,
  { params }: { params: Promise<{ id: string }> },
) {
  const { id } = await params;
  const body = await req.json().catch(() => ({}));
  return proxyToBackend({
    method: "PATCH",
    path: `/api/v1/facility/stations/${id}`,
    body,
    inbound: req,
  });
}
