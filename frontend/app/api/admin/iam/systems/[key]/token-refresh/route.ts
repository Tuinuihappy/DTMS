import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

type Ctx = { params: Promise<{ key: string }> };

// Save/enable/disable outbound-token auto-refresh config for a system.
export async function PUT(req: NextRequest, { params }: Ctx) {
  const { key } = await params;
  const body = await req.json().catch(() => ({}));
  return proxyToBackend({
    method: "PUT",
    path: `/api/v1/iam/systems/${encodeURIComponent(key)}/token-refresh`,
    body,
    inbound: req,
  });
}
