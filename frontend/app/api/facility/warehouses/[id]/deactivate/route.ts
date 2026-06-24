import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

// Soft-delete — backend keeps the row + cascades through any in-flight
// trips referencing the warehouse; only new order intake is blocked.
export async function POST(
  req: NextRequest,
  { params }: { params: Promise<{ id: string }> },
) {
  const { id } = await params;
  return proxyToBackend({
    method: "POST",
    path: `/api/v1/facility/warehouses/${id}/deactivate`,
    inbound: req,
  });
}
