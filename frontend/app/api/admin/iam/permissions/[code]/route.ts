import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

export async function PUT(
  req: NextRequest,
  { params }: { params: Promise<{ code: string }> },
) {
  const { code } = await params;
  const body = await req.json().catch(() => ({}));
  return proxyToBackend({
    method: "PUT",
    path: `/api/v1/iam/permissions/${encodeURIComponent(code)}`,
    body,
    inbound: req,
  });
}

export async function DELETE(
  req: NextRequest,
  { params }: { params: Promise<{ code: string }> },
) {
  const { code } = await params;
  return proxyToBackend({
    method: "DELETE",
    path: `/api/v1/iam/permissions/${encodeURIComponent(code)}`,
    inbound: req,
  });
}
