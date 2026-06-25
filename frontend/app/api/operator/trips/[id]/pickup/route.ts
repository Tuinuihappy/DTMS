import { type NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

export async function POST(
  req: NextRequest,
  { params }: { params: Promise<{ id: string }> },
) {
  const { id } = await params;
  const body = (await req.json().catch(() => ({}))) as unknown;
  return proxyToBackend({
    method: "POST",
    path: `/api/operator/trips/${encodeURIComponent(id)}/pickup`,
    body,
    inbound: req,
  });
}
