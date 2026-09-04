import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

type Ctx = { params: Promise<{ key: string }> };

// Point a system at another system's outbound token, or send a null
// tokenSourceKey to hand it back responsibility for minting its own.
export async function PUT(req: NextRequest, { params }: Ctx) {
  const { key } = await params;
  const body = await req.json().catch(() => ({}));
  return proxyToBackend({
    method: "PUT",
    path: `/api/v1/iam/systems/${encodeURIComponent(key)}/token-source`,
    body,
    inbound: req,
  });
}
