import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

type Ctx = { params: Promise<{ code: string }> };

// GET /api/fleet/carriers/{code}
export async function GET(_req: NextRequest, { params }: Ctx) {
  const { code } = await params;
  return proxyToBackend({
    method: "GET",
    path: `/api/v1/fleet/carriers/${encodeURIComponent(code)}`,
  });
}

// PUT /api/fleet/carriers/{code} → edit metadata
export async function PUT(req: NextRequest, { params }: Ctx) {
  const { code } = await params;
  const body = await req.json().catch(() => ({}));
  return proxyToBackend({
    method: "PUT",
    path: `/api/v1/fleet/carriers/${encodeURIComponent(code)}`,
    body,
    inbound: req,
  });
}

// DELETE /api/fleet/carriers/{code} → destroy the row.
// passthroughErrors keeps the backend's 409 and its reason intact: the delete
// guard explains what history is blocking, which the UI shows verbatim.
export async function DELETE(req: NextRequest, { params }: Ctx) {
  const { code } = await params;
  return proxyToBackend({
    method: "DELETE",
    path: `/api/v1/fleet/carriers/${encodeURIComponent(code)}`,
    inbound: req,
  });
}
