import { proxyToBackend } from "@/lib/api/proxy-helpers";

// GET /api/facility/carrier-type-profiles → backend list
export async function GET() {
  return proxyToBackend({
    method: "GET",
    path: "/api/v1/facility/carrier-type-profiles",
  });
}
