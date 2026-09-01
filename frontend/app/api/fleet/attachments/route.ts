import type { NextRequest } from "next/server";
import { proxyToBackend } from "@/lib/api/proxy-helpers";

const OWNERS = ["carrier", "carrier-type", "maintenance"] as const;

// The backend addresses a list as /attachments/{owner}/{ownerId} so each owner
// kind can carry its own permission in the route table. Next cannot mirror that
// shape here: a sibling [owner] and [id] at the same position is a build error,
// and DELETE needs the single-segment one. So the owner arrives as a query
// param and is translated back into a path here.
export async function GET(req: NextRequest) {
  const owner = req.nextUrl.searchParams.get("owner");
  const ownerId = req.nextUrl.searchParams.get("ownerId");

  if (!owner || !OWNERS.includes(owner as (typeof OWNERS)[number])) {
    return Response.json({ message: `Unknown owner '${owner}'.` }, { status: 400 });
  }
  if (!ownerId) {
    return Response.json({ message: "ownerId is required." }, { status: 400 });
  }

  return proxyToBackend({
    path: `/api/v1/fleet/attachments/${owner}/${encodeURIComponent(ownerId)}`,
  });
}
