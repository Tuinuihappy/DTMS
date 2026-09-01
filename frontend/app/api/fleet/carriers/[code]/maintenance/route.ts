import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

type Ctx = { params: Promise<{ code: string }> };

// GET /api/fleet/carriers/{code}/maintenance → repair history, newest first
export async function GET(_req: NextRequest, { params }: Ctx) {
  const { code } = await params;
  return proxyToBackend({
    method: "GET",
    path: `/api/v1/fleet/carriers/${encodeURIComponent(code)}/maintenance`,
  });
}

// POST /api/fleet/carriers/{code}/maintenance → take out of service
export async function POST(req: NextRequest, { params }: Ctx) {
  const { code } = await params;
  const body = await req.json().catch(() => ({}));
  return proxyToBackend({
    method: "POST",
    path: `/api/v1/fleet/carriers/${encodeURIComponent(code)}/maintenance`,
    body,
    inbound: req,
  });
}
