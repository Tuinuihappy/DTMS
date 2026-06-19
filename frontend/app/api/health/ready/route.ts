import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

// passthroughErrors: true — when the readiness probe returns 503 the body
// still contains the breakdown of which infra component is degraded. We
// want to surface that detail to the UI, not normalize it into a generic
// { message } error envelope.
export async function GET(req: NextRequest) {
  return proxyToBackend({
    method: "GET",
    path: "/health/ready",
    inbound: req,
    passthroughErrors: true,
  });
}
