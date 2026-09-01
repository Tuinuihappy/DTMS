import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

type Ctx = { params: Promise<{ code: string }> };

// POST /api/fleet/carriers/{code}/return-to-service → back in service, closing
// the open maintenance episode. A transition, so POST — DELETE stays reserved
// for the call that destroys the carrier.
export async function POST(req: NextRequest, { params }: Ctx) {
  const { code } = await params;
  const body = await req.json().catch(() => ({}));
  return proxyToBackend({
    method: "POST",
    path: `/api/v1/fleet/carriers/${encodeURIComponent(code)}/return-to-service`,
    body,
    inbound: req,
  });
}
