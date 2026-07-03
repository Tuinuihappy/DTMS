import { proxyToBackend } from "@/lib/api/proxy-helpers";

// WMS PR-4b (PR-D) — Server proxy for GET /api/operator/trips/pool.
// Cookie → bearer swap happens in proxyToBackend so the browser only
// carries the session cookie; the backend receives the operator's JWT.
export async function GET() {
  return proxyToBackend({ path: "/api/operator/trips/pool" });
}
