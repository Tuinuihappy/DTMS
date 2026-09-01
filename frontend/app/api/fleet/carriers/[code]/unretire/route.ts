import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

type Ctx = { params: Promise<{ code: string }> };

// POST /api/fleet/carriers/{code}/unretire → undo a retirement. Without this a
// mis-click is unrecoverable: a retired carrier can neither return to service
// (that path starts from Maintenance) nor be deleted (that needs Available).
export async function POST(req: NextRequest, { params }: Ctx) {
  const { code } = await params;
  return proxyToBackend({
    method: "POST",
    path: `/api/v1/fleet/carriers/${encodeURIComponent(code)}/unretire`,
    inbound: req,
  });
}
