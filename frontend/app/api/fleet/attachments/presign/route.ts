import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

export async function POST(req: NextRequest) {
  const body = (await req.json().catch(() => ({}))) as unknown;
  return proxyToBackend({
    method: "POST",
    path: "/api/v1/fleet/attachments/presign",
    body,
    inbound: req,
  });
}
