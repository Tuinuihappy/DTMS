import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

// The actions that occur in the audit log — feeds the Action filter.
export async function GET(req: NextRequest) {
  return proxyToBackend({
    method: "GET",
    path: "/api/v1/iam/audit-log/actions",
    inbound: req,
  });
}
