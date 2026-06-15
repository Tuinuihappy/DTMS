import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

type Ctx = { params: Promise<{ id: string }> };

// Phase P5.3 — proxy to backend GET /api/v1/dispatch/trips/{id}/items.
// Returns the items bound to this trip plus each item's owning order
// context (id / OrderRef / status). Sourced from the dispatch.TripItems
// read model materialized by TripItemsProjector.
export async function GET(_req: NextRequest, { params }: Ctx) {
  const { id } = await params;
  return proxyToBackend({
    method: "GET",
    path: `/api/v1/dispatch/trips/${id}/items`,
  });
}
