import { proxyToBackend } from "@/lib/api/proxy-helpers";

export async function GET() {
  return proxyToBackend({ path: "/api/operator/me" });
}
