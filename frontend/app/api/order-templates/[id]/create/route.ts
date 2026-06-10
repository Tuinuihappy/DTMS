import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

type Ctx = { params: Promise<{ id: string }> };

// Proxies the rename target /api/v1/order-templates/{id}/create — the
// endpoint resolves ActionTemplate refs against the catalog and POSTs
// the full envelope to RIOT3 (or returns the resolved preview when
// dryRun=true).
export async function POST(req: NextRequest, { params }: Ctx) {
  const { id } = await params;
  let body: unknown = undefined;
  try {
    body = await req.json();
  } catch {
    // optional body
  }
  return proxyToBackend({
    method: "POST",
    path: `/api/v1/order-templates/${id}/create`,
    body,
    inbound: req,
  });
}
