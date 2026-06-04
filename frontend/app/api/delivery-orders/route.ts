import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

export async function GET(req: NextRequest) {
  return proxyToBackend({
    method: "GET",
    path: "/api/v1/delivery-orders",
    search: req.nextUrl.searchParams,
  });
}

export async function POST(req: NextRequest) {
  let body: unknown = undefined;
  try {
    body = await req.json();
  } catch {
    // empty body is allowed; backend will reject if it needs one
  }
  return proxyToBackend({
    method: "POST",
    path: "/api/v1/delivery-orders",
    body,
    inbound: req,
  });
}
