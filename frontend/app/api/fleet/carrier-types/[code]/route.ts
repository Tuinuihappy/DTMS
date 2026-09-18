import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

type Ctx = { params: Promise<{ code: string }> };

// GET /api/fleet/carrier-types/{code}
export async function GET(_req: NextRequest, { params }: Ctx) {
  const { code } = await params;
  return proxyToBackend({
    method: "GET",
    path: `/api/v1/fleet/carrier-types/${encodeURIComponent(code)}`,
  });
}

// PUT /api/fleet/carrier-types/{code} → edit metadata. The code itself is not
// in the payload: it addresses the row and every carrier names its type by it.
export async function PUT(req: NextRequest, { params }: Ctx) {
  const { code } = await params;
  const body = await req.json().catch(() => ({}));
  return proxyToBackend({
    method: "PUT",
    path: `/api/v1/fleet/carrier-types/${encodeURIComponent(code)}`,
    body,
    inbound: req,
  });
}

// DELETE /api/fleet/carrier-types/{code} → destroy the row.
// The backend's 409 and its reason ("used by 4 carrier(s)") reach the UI intact,
// which is the whole value of the guard — it names what is blocking.
export async function DELETE(req: NextRequest, { params }: Ctx) {
  const { code } = await params;
  return proxyToBackend({
    method: "DELETE",
    path: `/api/v1/fleet/carrier-types/${encodeURIComponent(code)}`,
    inbound: req,
  });
}
