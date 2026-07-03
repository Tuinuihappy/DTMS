import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

// WMS PR-4 — proxy for the WMS location picker in the create-order dialog.
// Mirrors the warehouses/stations proxy pattern; the browser calls this
// route which forwards to /api/v1/wms/locations (authenticated by cookie
// → bearer at the proxy layer).

export async function GET(req: NextRequest) {
  const search = new URL(req.url).searchParams;
  return proxyToBackend({
    method: "GET",
    path: "/api/v1/wms/locations",
    search,
  });
}
