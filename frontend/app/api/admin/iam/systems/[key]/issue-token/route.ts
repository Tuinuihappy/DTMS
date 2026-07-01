import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

type Ctx = { params: Promise<{ key: string }> };

export async function POST(req: NextRequest, { params }: Ctx) {
  const { key } = await params;
  // Forward the inbound JSON body so the backend sees the operator's
  // lifetime override. proxyToBackend re-serializes whatever we pass as
  // `body`; reading it here gives us a plain object instead of a stream.
  const body = await req.json().catch(() => ({}));
  return proxyToBackend({
    method: "POST",
    path: `/api/v1/iam/systems/${encodeURIComponent(key)}/issue-token`,
    body,
    inbound: req,
  });
}
