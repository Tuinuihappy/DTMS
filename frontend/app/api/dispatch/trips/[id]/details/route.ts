import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

type Ctx = { params: Promise<{ id: string }> };

export async function GET(req: NextRequest, { params }: Ctx) {
  const { id } = await params;
  const includeRaw = req.nextUrl.searchParams.get("includeRaw");
  const search = includeRaw === "true" ? "includeRaw=true" : "";
  return proxyToBackend({
    method: "GET",
    path: `/api/v1/dispatch/trips/${id}/details`,
    search,
  });
}
