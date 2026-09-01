import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

type Ctx = { params: Promise<{ code: string }> };

// PUT /api/fleet/carriers/{code}/location → record where it was last seen.
// Allowed in every status: a cart under repair or already retired still moves.
export async function PUT(req: NextRequest, { params }: Ctx) {
  const { code } = await params;
  const body = await req.json().catch(() => ({}));
  return proxyToBackend({
    method: "PUT",
    path: `/api/v1/fleet/carriers/${encodeURIComponent(code)}/location`,
    body,
    inbound: req,
  });
}
