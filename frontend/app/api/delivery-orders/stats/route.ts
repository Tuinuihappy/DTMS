import { proxyToBackend } from "@/lib/api/proxy-helpers";

export async function GET() {
  return proxyToBackend({
    method: "GET",
    path: "/api/v1/delivery-orders/stats",
  });
}
