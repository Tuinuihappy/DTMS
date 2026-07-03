import { type NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

export async function POST(
  req: NextRequest,
  { params }: { params: Promise<{ id: string }> },
) {
  const { id } = await params;
  return proxyToBackend({
    method: "POST",
    path: `/api/operator/trips/${encodeURIComponent(id)}/acknowledge`,
    inbound: req,
    // WMS PR-4b (PR-D) — pass the backend's { Error: "TRIP_ALREADY_CLAIMED" }
    // body through untouched on 409 so the PWA can distinguish a race
    // conflict (toast "someone else took it") from a generic failure.
    passthroughErrors: true,
  });
}
