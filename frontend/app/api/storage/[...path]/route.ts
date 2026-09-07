import "server-only";

import { NextResponse, type NextRequest } from "next/server";
import { getServerToken } from "@/lib/auth/server-session";
import { STORAGE_INTERNAL_ORIGIN, STORAGE_PROXY_PREFIX } from "@/lib/api/storage-origin";

// Relays image traffic between the browser and MinIO so the browser only ever
// talks to the origin it loaded the page from.
//
// Follows the shape of app/api/reports/orders-export/route.ts, which already
// forwards a raw stream rather than going through the JSON proxy helper.
//
// This route does NOT authorise anything. The signature in the query string is
// what MinIO accepts or refuses, and it was minted by the API after a
// permission check. The session check here only stops the proxy being an open
// relay for anyone holding a leaked URL.

// A 10 MiB upload over warehouse wifi outlasts the 20s the JSON proxy allows.
const TIMEOUT_MS = 120_000;

// Guards the path shape, not access — MinIO still demands a valid signature.
const BUCKET = /^dtms-[a-z0-9-]{1,50}$/;

// Response headers worth carrying back. Content-Type matters most: the signed
// URL forces it via response-content-type, which is what stops an object
// uploaded as SVG from being served as one.
const PASS_THROUGH = [
  "content-type",
  "content-length",
  "content-range",
  "accept-ranges",
  "etag",
  "last-modified",
];

export async function GET(req: NextRequest) {
  return relay(req);
}

export async function POST(req: NextRequest) {
  return relay(req);
}

async function relay(req: NextRequest): Promise<NextResponse> {
  // 401, never a redirect. check-route-protection.mjs states the contract for
  // everything under /api, and it matters here specifically: an <img> that
  // follows a redirect gets the login page's HTML and reports a broken image
  // instead of a status the caller can act on.
  if (!(await getServerToken())) {
    return NextResponse.json({ message: "Not signed in." }, { status: 401 });
  }

  // Rebuilt from the raw pathname, never from the [...path] params: Next hands
  // those over already percent-decoded, and re-encoding them can produce a
  // different string than the one that was signed. SigV4 covers the canonical
  // URI, so that difference is a 403 on some keys and not others — the kind of
  // bug that reproduces only for a filename with a space in it.
  const rest = req.nextUrl.pathname.slice(STORAGE_PROXY_PREFIX.length);
  const bucket = rest.split("/").filter(Boolean)[0];
  if (!bucket || !BUCKET.test(bucket)) {
    return NextResponse.json({ message: "Unknown storage bucket." }, { status: 400 });
  }

  const target = `${STORAGE_INTERNAL_ORIGIN}${rest}${req.nextUrl.search}`;

  const headers = new Headers();
  // Range so a lightbox or a future large file can ask for part of an object;
  // Content-Type because a multipart upload is unparseable without the exact
  // boundary the browser generated.
  for (const name of ["range", "content-type"]) {
    const value = req.headers.get(name);
    if (value) headers.set(name, value);
  }

  // Buffered, not streamed. A chunked body carries no Content-Length, and MinIO
  // then refuses the upload outright — measured: a policy-conforming 512-byte
  // file streamed with duplex:"half" came back 400, while the same bytes sent
  // buffered came back 204. The upload policy caps size at 10 MiB, so what is
  // held in memory here is bounded by the same limit that makes it safe.
  const body =
    req.method === "POST" ? await req.arrayBuffer() : undefined;

  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), TIMEOUT_MS);

  let upstream: Response;
  try {
    upstream = await fetch(target, {
      method: req.method,
      headers,
      body,
      cache: "no-store",
      signal: controller.signal,
    });
  } catch (err) {
    const aborted = (err as { name?: string })?.name === "AbortError";
    console.error(`[storage-proxy] ${req.method} ${target} ${aborted ? "TIMEOUT" : "ERROR"}:`, err);
    return NextResponse.json(
      { message: aborted ? "Storage didn't respond in time." : "Couldn't reach storage." },
      { status: aborted ? 504 : 502 },
    );
  } finally {
    clearTimeout(timer);
  }

  const out = new Headers();
  for (const name of PASS_THROUGH) {
    const value = upstream.headers.get(name);
    if (value) out.set(name, value);
  }

  // Upstream status verbatim: a 206 must stay a 206 or a ranged request gets a
  // full body it did not ask for, and MinIO's own 400/403 should reach the
  // caller rather than being recast as something friendlier and less true.
  return new NextResponse(upstream.body, { status: upstream.status, headers: out });
}
