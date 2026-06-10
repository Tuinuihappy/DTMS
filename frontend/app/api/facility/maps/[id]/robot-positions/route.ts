import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

export async function GET(
  req: NextRequest,
  { params }: { params: Promise<{ id: string }> },
) {
  const { id } = await params;
  return proxyToBackend({
    method: "GET",
    path: `/api/v1/facility/maps/${id}/robot-positions`,
    inbound: req,
  });
}
