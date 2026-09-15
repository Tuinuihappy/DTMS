import type { NextRequest } from "next/server";
import { relayAttachmentImage } from "@/lib/api/attachment-image-relay";

// The full-size image under a stable, cacheable address, so opening a photo a
// second time costs nothing. See attachment-image-relay.
export async function GET(
  req: NextRequest,
  { params }: { params: Promise<{ id: string }> },
) {
  const { id } = await params;
  return relayAttachmentImage(req, id, "image");
}
