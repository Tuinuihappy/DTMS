import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

type Ctx = { params: Promise<{ orderId: string }> };

export async function GET(_req: NextRequest, { params }: Ctx) {
  const { orderId } = await params;
  return proxyToBackend({
    method: "GET",
    path: `/api/v1/dispatch/orders/${orderId}/trips`,
  });
}
