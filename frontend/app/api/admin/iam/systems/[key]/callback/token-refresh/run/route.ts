import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

type Ctx = { params: Promise<{ key: string }> };

// Manual "refresh now" — mint + swap the outbound callback token immediately.
export async function POST(req: NextRequest, { params }: Ctx) {
  const { key } = await params;
  return proxyToBackend({
    method: "POST",
    path: `/api/v1/iam/systems/${encodeURIComponent(key)}/callback/token-refresh/run`,
    inbound: req,
  });
}
