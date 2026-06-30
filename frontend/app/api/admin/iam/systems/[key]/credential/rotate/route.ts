import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

type Ctx = { params: Promise<{ key: string }> };

export async function POST(req: NextRequest, { params }: Ctx) {
  const { key } = await params;
  return proxyToBackend({
    method: "POST",
    path: `/api/v1/iam/systems/${encodeURIComponent(key)}/credential/rotate`,
    inbound: req,
  });
}
