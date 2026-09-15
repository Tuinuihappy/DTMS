import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";
import { isAttachmentOwner, isGuid, ownerPath } from "@/lib/api/attachment-owners";

// The owner travels in the body from the browser but in the path to the API,
// where it decides which write permission guards the upload. Only the rest of
// the body is passed on, so the API never sees an owner it could disagree with.
export async function POST(req: NextRequest) {
  const { owner, ownerId, ...rest } = (await req.json().catch(() => ({}))) as Record<string, unknown>;

  if (!isAttachmentOwner(owner) || !isGuid(ownerId)) {
    return Response.json({ message: "A known owner and a GUID ownerId are required." }, { status: 400 });
  }

  return proxyToBackend({
    method: "POST",
    path: `${ownerPath(owner, ownerId)}/presign`,
    body: rest,
    inbound: req,
  });
}
