import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

type Ctx = { params: Promise<{ code: string }> };

// POST /api/fleet/carriers/{code}/retire → end of life. The row, its history,
// and its code reservation all stay; un-retire is the way back.
export async function POST(req: NextRequest, { params }: Ctx) {
  const { code } = await params;
  const body = await req.json().catch(() => ({}));
  return proxyToBackend({
    method: "POST",
    path: `/api/v1/fleet/carriers/${encodeURIComponent(code)}/retire`,
    body,
    inbound: req,
  });
}
