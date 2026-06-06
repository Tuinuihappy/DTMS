import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

type Ctx = { params: Promise<{ id: string }> };

export async function POST(req: NextRequest, { params }: Ctx) {
  const { id } = await params;
  let body: unknown = undefined;
  try {
    body = await req.json();
  } catch {
    // empty body OK — backend treats missing as defaults
  }
  return proxyToBackend({
    method: "POST",
    path: `/api/v1/dispatch/trips/${id}/retry`,
    body,
    inbound: req,
  });
}
