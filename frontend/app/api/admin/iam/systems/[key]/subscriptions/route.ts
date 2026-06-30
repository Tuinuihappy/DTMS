import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

type Ctx = { params: Promise<{ key: string }> };

export async function GET(req: NextRequest, { params }: Ctx) {
  const { key } = await params;
  return proxyToBackend({
    method: "GET",
    path: `/api/v1/iam/systems/${encodeURIComponent(key)}/subscriptions`,
    inbound: req,
  });
}

export async function POST(req: NextRequest, { params }: Ctx) {
  const { key } = await params;
  const body = await req.json().catch(() => ({}));
  return proxyToBackend({
    method: "POST",
    path: `/api/v1/iam/systems/${encodeURIComponent(key)}/subscriptions`,
    body,
    inbound: req,
  });
}
