import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

type Ctx = { params: Promise<{ key: string }> };

// Set or clear the token DTMS presents when it calls this system. Separate from
// [key]/callback so saving one cannot wipe the other.
export async function PUT(req: NextRequest, { params }: Ctx) {
  const { key } = await params;
  const body = await req.json().catch(() => ({}));
  return proxyToBackend({
    method: "PUT",
    path: `/api/v1/iam/systems/${encodeURIComponent(key)}/outbound-token`,
    body,
    inbound: req,
  });
}
