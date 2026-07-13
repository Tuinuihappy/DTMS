import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

// The permission catalog is code-defined and read-only — GET only.
export async function GET(req: NextRequest) {
  return proxyToBackend({
    method: "GET",
    path: "/api/v1/iam/permissions",
    inbound: req,
  });
}
