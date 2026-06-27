import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

export async function GET(req: NextRequest) {
  const search = new URL(req.url).searchParams;
  return proxyToBackend({
    method: "GET",
    path: "/api/v1/iam/audit-log",
    search,
    inbound: req,
  });
}
