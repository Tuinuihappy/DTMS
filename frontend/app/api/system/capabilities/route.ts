import { proxyToBackend } from "@/lib/api/proxy-helpers";

// WMS PR-4 — proxies the anonymous capabilities probe. Called by
// the create-order dialog on mount so it can hide Manual/Fleet
// transport options when the underlying feature flag is off.

export async function GET() {
  return proxyToBackend({
    method: "GET",
    path: "/api/v1/system/capabilities",
  });
}
