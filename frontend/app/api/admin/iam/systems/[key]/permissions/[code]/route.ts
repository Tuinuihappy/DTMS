import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

export async function POST(
  req: NextRequest,
  { params }: { params: Promise<{ key: string; code: string }> },
) {
  const { key, code } = await params;
  return proxyToBackend({
    method: "POST",
    path: `/api/v1/iam/systems/${encodeURIComponent(key)}/permissions/${encodeURIComponent(code)}`,
    inbound: req,
  });
}

export async function DELETE(
  req: NextRequest,
  { params }: { params: Promise<{ key: string; code: string }> },
) {
  const { key, code } = await params;
  return proxyToBackend({
    method: "DELETE",
    path: `/api/v1/iam/systems/${encodeURIComponent(key)}/permissions/${encodeURIComponent(code)}`,
    inbound: req,
  });
}
