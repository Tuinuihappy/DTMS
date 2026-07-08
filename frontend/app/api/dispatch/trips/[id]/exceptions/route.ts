import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

type Ctx = { params: Promise<{ id: string }> };

// POST /api/dispatch/trips/{id}/exceptions → backend raise trip exception
// Body: { code, severity, detail }. Returns 201 + new exception id.
export async function POST(req: NextRequest, { params }: Ctx) {
  const { id } = await params;
  const body = await req.json().catch(() => ({}));
  return proxyToBackend({
    method: "POST",
    path: `/api/v1/dispatch/trips/${id}/exceptions`,
    body,
    inbound: req,
  });
}
