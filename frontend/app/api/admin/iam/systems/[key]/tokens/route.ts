import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

type Ctx = { params: Promise<{ key: string }> };

export async function GET(_req: NextRequest, { params }: Ctx) {
  const { key } = await params;
  return proxyToBackend({
    method: "GET",
    path: `/api/v1/iam/systems/${encodeURIComponent(key)}/tokens`,
  });
}
