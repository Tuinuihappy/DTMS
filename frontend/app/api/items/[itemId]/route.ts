import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

// GET /api/items/{itemId} → backend GET /api/v1/items/{itemId} (item detail)
export async function GET(
  _req: NextRequest,
  { params }: { params: Promise<{ itemId: string }> },
) {
  const { itemId } = await params;
  return proxyToBackend({
    method: "GET",
    path: `/api/v1/items/${itemId}`,
  });
}
