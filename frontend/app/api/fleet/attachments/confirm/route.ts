import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";
import { isAttachmentOwner, isGuid, ownerPath } from "@/lib/api/attachment-owners";

// Same translation as presign. The owner here is the one the image lands on, so
// it is this request's owner — not the presign's — that the API checks write on.
export async function POST(req: NextRequest) {
  const { owner, ownerId, ...rest } = (await req.json().catch(() => ({}))) as Record<string, unknown>;

  if (!isAttachmentOwner(owner) || !isGuid(ownerId)) {
    return Response.json({ message: "A known owner and a GUID ownerId are required." }, { status: 400 });
  }

  return proxyToBackend({
    method: "POST",
    path: `${ownerPath(owner, ownerId)}/confirm`,
    body: rest,
    inbound: req,
  });
}
