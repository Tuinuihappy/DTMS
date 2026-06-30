import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

type Ctx = { params: Promise<{ key: string; eventType: string }> };

export async function PATCH(req: NextRequest, { params }: Ctx) {
  const { key, eventType } = await params;
  const body = await req.json().catch(() => ({}));
  return proxyToBackend({
    method: "PATCH",
    path: `/api/v1/iam/systems/${encodeURIComponent(key)}/subscriptions/${encodeURIComponent(eventType)}`,
    body,
    inbound: req,
  });
}

export async function DELETE(req: NextRequest, { params }: Ctx) {
  const { key, eventType } = await params;
  return proxyToBackend({
    method: "DELETE",
    path: `/api/v1/iam/systems/${encodeURIComponent(key)}/subscriptions/${encodeURIComponent(eventType)}`,
    inbound: req,
  });
}
