import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

// Static `standard` segment wins over the sibling dynamic `[code]` route,
// so this never collides with the grant/revoke proxy.
export async function GET(
  req: NextRequest,
  { params }: { params: Promise<{ key: string }> },
) {
  const { key } = await params;
  return proxyToBackend({
    method: "GET",
    path: `/api/v1/iam/systems/${encodeURIComponent(key)}/permissions/standard`,
    inbound: req,
  });
}
