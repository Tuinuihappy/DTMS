import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

type Ctx = { params: Promise<{ id: string }> };

export async function POST(req: NextRequest, { params }: Ctx) {
  const { id } = await params;
  return proxyToBackend({
    method: "POST",
    path: `/api/v1/planning/jobs/${id}/retry`,
    inbound: req,
  });
}
