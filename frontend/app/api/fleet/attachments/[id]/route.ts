import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

export async function DELETE(
  req: NextRequest,
  { params }: { params: Promise<{ id: string }> },
) {
  const { id } = await params;
  return proxyToBackend({
    method: "DELETE",
    path: `/api/v1/fleet/attachments/${encodeURIComponent(id)}`,
    inbound: req,
  });
}
