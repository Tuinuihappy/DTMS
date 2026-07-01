import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

type Ctx = { params: Promise<{ key: string; jti: string }> };

export async function POST(req: NextRequest, { params }: Ctx) {
  const { key, jti } = await params;
  // Forward the optional { reason } body so audit + UI show why the
  // token was revoked.
  const body = await req.json().catch(() => ({}));
  return proxyToBackend({
    method: "POST",
    path: `/api/v1/iam/systems/${encodeURIComponent(key)}/tokens/${encodeURIComponent(jti)}/revoke`,
    body,
    inbound: req,
  });
}
