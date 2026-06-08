import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

export async function GET(req: NextRequest) {
  return proxyToBackend({
    method: "GET",
    path: "/api/v1/action-templates",
    search: req.nextUrl.searchParams,
  });
}

export async function POST(req: NextRequest) {
  let body: unknown = undefined;
  try {
    body = await req.json();
  } catch {
    // empty body — backend will reject
  }
  return proxyToBackend({
    method: "POST",
    path: "/api/v1/action-templates",
    body,
    inbound: req,
  });
}
