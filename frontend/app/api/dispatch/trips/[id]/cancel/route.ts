import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

type Ctx = { params: Promise<{ id: string }> };

export async function POST(req: NextRequest, { params }: Ctx) {
  const { id } = await params;
  // Backend wants the reason as a query string ([FromQuery] string reason).
  const reason = req.nextUrl.searchParams.get("reason") ?? "Cancelled by operator.";
  return proxyToBackend({
    method: "POST",
    path: `/api/v1/dispatch/trips/${id}/cancel`,
    search: `reason=${encodeURIComponent(reason)}`,
    inbound: req,
  });
}
