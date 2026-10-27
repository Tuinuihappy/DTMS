import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

export async function POST(
  req: NextRequest,
  { params }: { params: Promise<{ id: string }> },
) {
  const { id } = await params;
  // Only confirmMassDeactivation is forwarded — it releases a held batch.
  const search = new URLSearchParams();
  if (req.nextUrl.searchParams.get("confirmMassDeactivation") === "true")
    search.set("confirmMassDeactivation", "true");
  return proxyToBackend({
    method: "POST",
    path: `/api/v1/facility/maps/${id}/sync-stations`,
    search,
    inbound: req,
  });
}
