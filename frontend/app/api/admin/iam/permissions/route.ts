import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

export async function GET(req: NextRequest) {
  return proxyToBackend({
    method: "GET",
    path: "/api/v1/iam/permissions",
    inbound: req,
  });
}

export async function POST(req: NextRequest) {
  const body = await req.json().catch(() => ({}));
  return proxyToBackend({
    method: "POST",
    path: "/api/v1/iam/permissions",
    body,
    inbound: req,
  });
}
