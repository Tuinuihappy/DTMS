import { type NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

export async function POST(req: NextRequest) {
  return proxyToBackend({
    method: "POST",
    path: "/api/operator/push/test",
    inbound: req,
  });
}
