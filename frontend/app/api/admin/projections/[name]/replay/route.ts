import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

type Ctx = { params: Promise<{ name: string }> };

export async function POST(req: NextRequest, { params }: Ctx) {
  const { name } = await params;
  let body: unknown = {};
  try {
    body = await req.json();
  } catch {
    // body required by backend — fall through; backend returns 400 with
    // a clear "FromUtc is required" message.
  }
  return proxyToBackend({
    method: "POST",
    path: `/api/v1/admin/projections/${encodeURIComponent(name)}/replay`,
    body,
    inbound: req,
    // Pass the 501/400 status through verbatim so the dialog can surface
    // the backend's "not yet implemented" message instead of a generic
    // proxy-normalised JSON.
    passthroughErrors: true,
  });
}
