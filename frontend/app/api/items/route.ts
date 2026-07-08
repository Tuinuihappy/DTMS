import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

// GET /api/items → backend GET /api/v1/items (paged item search)
export async function GET(req: NextRequest) {
  const search = new URL(req.url).searchParams;
  return proxyToBackend({
    method: "GET",
    path: "/api/v1/items",
    search,
  });
}
