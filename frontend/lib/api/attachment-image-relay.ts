import "server-only";

import { NextResponse, type NextRequest } from "next/server";
import { getServerToken } from "@/lib/auth/server-session";
import { STORAGE_INTERNAL_ORIGIN } from "@/lib/api/storage-origin";
import { isAttachmentOwner, isGuid, ownerPath } from "@/lib/api/attachment-owners";

// An image under an address that never changes, served with bytes the browser
// may keep for good. Backs both the thumbnail and the full-size routes.
//
// Signed storage URLs are the wrong thing to put in an <img>: the signature
// carries a timestamp, so every list call mints a different URL, the browser
// treats each as a new file, and a table of photos re-downloads itself every
// time someone touches a filter. An attachment's image never changes — a new
// upload gets a new id — so the id is a perfectly good cache key. This relay
// asks the API where the bytes are, fetches them, and hands them back as its own.
//
// It fetches rather than passing the API's redirect on. A browser that cached a
// redirect for a year would keep following it long after the signature inside
// had expired; caching the bytes has no such shelf life.
//
// Goes around proxyToBackend on purpose: that helper reads bodies as text and
// follows redirects, which is exactly wrong for both halves of this.

/** The API's last path segment for each size. */
export type AttachmentImageVariant = "thumbnail" | "image";

// Anything slower than this is a stuck request, not a slow one — the storage
// relay's two minutes exist for uploads. A full image is at most 1600px of
// JPEG, a few hundred KB, so it gets more room than a thumbnail but not minutes.
const TIMEOUT_MS: Record<AttachmentImageVariant, number> = {
  thumbnail: 15_000,
  image: 30_000,
};

// Only ever sent with a 200 that is actually an image. "private" because the
// route is behind a session; no Vary: Cookie, since every token refresh would
// then miss the cache and defeat the point.
const CACHEABLE = "private, max-age=31536000, immutable";

const PASS_THROUGH = ["content-type", "content-length", "etag", "last-modified"];

export async function relayAttachmentImage(
  req: NextRequest,
  id: string,
  variant: AttachmentImageVariant,
): Promise<NextResponse> {
  const owner = req.nextUrl.searchParams.get("owner");
  const ownerId = req.nextUrl.searchParams.get("ownerId");

  if (!isGuid(id) || !isGuid(ownerId) || !isAttachmentOwner(owner)) {
    return fail(400, "Bad image request.");
  }

  const base = process.env.DTMS_BACKEND_URL;
  if (!base) return fail(500, "Server misconfigured: DTMS_BACKEND_URL is not set.");

  // 401, never a redirect: an <img> that follows one gets the login page's HTML
  // and shows a broken image instead of a status anyone can act on.
  const token = await getServerToken();
  if (!token) return fail(401, "Not signed in.");

  const signal = AbortSignal.timeout(TIMEOUT_MS[variant]);

  try {
    const api = await fetch(
      `${base.replace(/\/$/, "")}${ownerPath(owner, ownerId)}/${id}/${variant}`,
      {
        headers: { Authorization: `Bearer ${token}` },
        // Manual, so the 302 comes back as a response with a readable Location
        // (Node's fetch returns the real 3xx, unlike a browser's opaque one).
        redirect: "manual",
        cache: "no-store",
        signal,
      },
    );
    const location = api.headers.get("location");
    // The redirect's body is never read; release the connection instead of
    // leaving it held open.
    await api.body?.cancel();

    if (api.status !== 302) {
      // The API's own refusals mean something to the caller; anything else — a
      // stray 307, a 500 — is this route's failure to get an image.
      const passOn = api.status === 401 || api.status === 403 || api.status === 404;
      return fail(passOn ? api.status : 502, "Image unavailable.");
    }

    // Only ever follow a redirect into our own storage. The trailing slash
    // matters: without it "http://minio:9000.attacker" would pass.
    if (!location?.startsWith(`${STORAGE_INTERNAL_ORIGIN}/`)) {
      return fail(502, "Unexpected storage location.");
    }

    // The raw Location string, never re-parsed through new URL(): re-encoding
    // can change the canonical URI the signature covers. And no Authorization —
    // MinIO refuses a request that carries both a bearer token and a query
    // signature.
    const object = await fetch(location, { cache: "no-store", signal });
    const type = object.headers.get("content-type") ?? "";

    // Cacheable only if this is genuinely an image. MinIO's error bodies are
    // XML; letting one through under an immutable header would pin a broken
    // image to this URL for a year.
    if (object.status !== 200 || !type.startsWith("image/")) {
      await object.body?.cancel();
      return fail(object.status === 404 ? 404 : 502, "Image unavailable.");
    }

    const out = new Headers({
      "Cache-Control": CACHEABLE,
      // Served from the app's own origin, so the browser must not second-guess
      // the image type and render something else.
      "X-Content-Type-Options": "nosniff",
    });
    for (const name of PASS_THROUGH) {
      const value = object.headers.get(name);
      if (value) out.set(name, value);
    }

    return new NextResponse(object.body, { status: 200, headers: out });
  } catch (err) {
    const timedOut = (err as { name?: string })?.name === "TimeoutError";
    console.error(`[${variant}] ${owner}/${ownerId}/${id} ${timedOut ? "TIMEOUT" : "ERROR"}:`, err);
    return fail(
      timedOut ? 504 : 502,
      timedOut ? "Storage didn't respond in time." : "Couldn't reach storage.",
    );
  }
}

/**
 * Every failure is no-store. This URL is marked immutable when it succeeds, so a
 * 401 or 404 that got cached here would stay wrong for as long as the image would
 * have stayed right.
 */
function fail(status: number, message: string): NextResponse {
  return NextResponse.json({ message }, { status, headers: { "Cache-Control": "no-store" } });
}
