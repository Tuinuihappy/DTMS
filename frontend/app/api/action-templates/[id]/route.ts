import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

type Ctx = { params: Promise<{ id: string }> };

export async function GET(_req: NextRequest, { params }: Ctx) {
  const { id } = await params;
  return proxyToBackend({
    method: "GET",
    path: `/api/v1/action-templates/${id}`,
  });
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
    path: `/api/v1/action-templates/${id}`,
    body,
    inbound: req,
  });
}

export async function DELETE(req: NextRequest, { params }: Ctx) {
  const { id } = await params;
  return proxyToBackend({
    method: "DELETE",
    path: `/api/v1/action-templates/${id}`,
    inbound: req,
  });
}
