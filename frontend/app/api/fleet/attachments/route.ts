import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";
import { isAttachmentOwner, isGuid, ownerPath } from "@/lib/api/attachment-owners";

// The backend addresses a list as /attachments/{owner}/{ownerId} so each owner
// kind can carry its own permission in the route table. Next cannot mirror that
// shape here: a sibling [owner] and [id] at the same position is a build error,
// and DELETE needs the single-segment one. So the owner arrives as a query
// param and is translated back into a path here.
export async function GET(req: NextRequest) {
  const owner = req.nextUrl.searchParams.get("owner");
  const ownerId = req.nextUrl.searchParams.get("ownerId");

  if (!isAttachmentOwner(owner)) {
    return Response.json({ message: `Unknown owner '${owner}'.` }, { status: 400 });
  }
  if (!isGuid(ownerId)) {
    return Response.json({ message: "ownerId must be a GUID." }, { status: 400 });
  }

  return proxyToBackend({ path: ownerPath(owner, ownerId) });
}
