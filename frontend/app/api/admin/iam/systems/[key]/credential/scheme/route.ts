import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

type Ctx = { params: Promise<{ key: string }> };

// PUT /api/admin/iam/systems/{key}/credential/scheme → backend switch auth scheme
export async function PUT(req: NextRequest, { params }: Ctx) {
  const { key } = await params;
  const body = await req.json().catch(() => ({}));
  return proxyToBackend({
    method: "PUT",
    path: `/api/v1/iam/systems/${encodeURIComponent(key)}/credential/scheme`,
    body,
    inbound: req,
  });
}
