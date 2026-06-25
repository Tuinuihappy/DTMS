import { type NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

export async function POST(
  req: NextRequest,
  { params }: { params: Promise<{ tripId: string }> },
) {
  const { tripId } = await params;
  const body = (await req.json().catch(() => ({}))) as unknown;
  return proxyToBackend({
    method: "POST",
    path: `/api/v1/admin/manual/trips/${encodeURIComponent(tripId)}/reassign`,
    body,
    inbound: req,
  });
}
