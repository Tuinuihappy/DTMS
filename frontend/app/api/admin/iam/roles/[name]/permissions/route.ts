import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

export async function GET(
  req: NextRequest,
  { params }: { params: Promise<{ name: string }> },
) {
  const { name } = await params;
  return proxyToBackend({
    method: "GET",
    path: `/api/v1/iam/roles/${encodeURIComponent(name)}/permissions`,
    inbound: req,
  });
}
