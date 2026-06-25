import { type NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

export async function POST(
  req: NextRequest,
  { params }: { params: Promise<{ id: string }> },
) {
  const { id } = await params;
  return proxyToBackend({
    method: "POST",
    path: `/api/operator/trips/${encodeURIComponent(id)}/complete`,
    inbound: req,
  });
}
