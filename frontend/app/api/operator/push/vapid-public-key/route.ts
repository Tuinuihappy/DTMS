import { proxyToBackend } from "@/lib/api/proxy-helpers";

// Anonymous on the backend; proxied so the PWA can fetch it via the
// same origin without CORS gymnastics. No token attached server-side
// either (proxy attaches it only if present in the cookie).
export async function GET() {
  return proxyToBackend({ path: "/api/operator/push/vapid-public-key" });
}
