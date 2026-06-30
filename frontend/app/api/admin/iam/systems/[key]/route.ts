import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

export async function GET(req: NextRequest, { params }: { params: Promise<{ key: string }> }) {
  const { key } = await params;
  return proxyToBackend({
    method: "GET",
    path: `/api/v1/iam/systems/${encodeURIComponent(key)}`,
    inbound: req,
  });
}

export async function PATCH(req: NextRequest, { params }: { params: Promise<{ key: string }> }) {
  const { key } = await params;
  const body = await req.json().catch(() => ({}));
  return proxyToBackend({
    method: "PATCH",
    path: `/api/v1/iam/systems/${encodeURIComponent(key)}`,
    body,
    inbound: req,
  });
}
