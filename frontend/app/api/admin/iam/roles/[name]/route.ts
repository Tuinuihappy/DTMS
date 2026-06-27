import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

export async function DELETE(
  req: NextRequest,
  { params }: { params: Promise<{ name: string }> },
) {
  const { name } = await params;
  return proxyToBackend({
    method: "DELETE",
    path: `/api/v1/iam/roles/${encodeURIComponent(name)}`,
    inbound: req,
  });
}
