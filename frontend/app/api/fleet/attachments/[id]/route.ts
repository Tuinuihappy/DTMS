import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";
import { isAttachmentOwner, isGuid, ownerPath } from "@/lib/api/attachment-owners";

// The owner comes as query params for the same reason as the list route, and
// becomes part of the API path. The API refuses an image that is not that
// owner's, so naming the wrong owner is a 404, not a way around a permission.
export async function DELETE(
  req: NextRequest,
  { params }: { params: Promise<{ id: string }> },
) {
  const { id } = await params;
  const owner = req.nextUrl.searchParams.get("owner");
  const ownerId = req.nextUrl.searchParams.get("ownerId");

  if (!isGuid(id) || !isAttachmentOwner(owner) || !isGuid(ownerId)) {
    return Response.json({ message: "A known owner, a GUID ownerId and a GUID id are required." }, { status: 400 });
  }

  return proxyToBackend({
    method: "DELETE",
    path: `${ownerPath(owner, ownerId)}/${id}`,
    inbound: req,
  });
}
