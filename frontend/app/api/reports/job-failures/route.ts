import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

export async function GET(req: NextRequest) {
  return proxyToBackend({
    method: "GET",
    path: "/api/v1/reports/job-failures",
    search: req.nextUrl.searchParams,
    inbound: req,
  });
}
