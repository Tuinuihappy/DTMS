import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

type Ctx = { params: Promise<{ id: string }> };

export async function GET(_req: NextRequest, { params }: Ctx) {
  const { id } = await params;
  return proxyToBackend({ method: "GET", path: `/api/v1/delivery-orders/${id}` });
}

export async function PUT(req: NextRequest, { params }: Ctx) {
  const { id } = await params;
  let body: unknown = undefined;
  try {
    body = await req.json();
  } catch {
    // ignore
  }
  return proxyToBackend({
    method: "PUT",
    path: `/api/v1/delivery-orders/${id}`,
    body,
    inbound: req,
  });
}

export async function DELETE(req: NextRequest, { params }: Ctx) {
  const { id } = await params;
  // Backend requires { reason } in the body — surface it through the
  // proxy. If the client sends nothing, default to a marker so the
  // backend's [FromBody] binder doesn't reject the request outright.
  let body: unknown = { reason: "Cancelled by user." };
  try {
    const parsed = await req.json();
    if (parsed && typeof parsed === "object") body = parsed;
  } catch {
    // keep default
  }
  return proxyToBackend({
    method: "DELETE",
    path: `/api/v1/delivery-orders/${id}`,
    body,
    inbound: req,
  });
}
