import "server-only";

import { type NextRequest, NextResponse } from "next/server";
import { getServerToken } from "@/lib/auth/server-session";
import { errorMessageFrom } from "@/lib/api/errors";

// How long to wait for the backend to START answering. Once headers arrive
// the CSV streams straight through, however long the file takes.
const HEADERS_TIMEOUT_MS = 60_000;

/**
 * Streams a backend CSV export to the browser with its Content-Disposition,
 * so the browser downloads it. Bypasses proxyToBackend on purpose: that
 * helper reads the body as text/JSON, which would buffer the whole file.
 *
 * The three report routes used to each call fetch() bare (review M11): a
 * backend that was down threw an unhandled rejection, one that hung kept
 * the request open forever, and a refusal came back as "Export failed:
 * 403" with the backend's reason dropped.
 */
export async function proxyCsvExport(
  req: NextRequest,
  backendPath: string,
  fallbackFilename: string,
): Promise<NextResponse> {
  const base = process.env.DTMS_BACKEND_URL;
  if (!base) {
    return NextResponse.json(
      { message: "Server misconfigured: DTMS_BACKEND_URL is not set." },
      { status: 500 },
    );
  }

  const token = await getServerToken();
  const qs = req.nextUrl.searchParams.toString();
  const url = `${base.replace(/\/$/, "")}${backendPath}${qs ? `?${qs}` : ""}`;

  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), HEADERS_TIMEOUT_MS);
  let upstream: Response;
  try {
    upstream = await fetch(url, {
      method: "GET",
      headers: {
        Accept: "text/csv",
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
      },
      cache: "no-store",
      signal: controller.signal,
    });
  } catch (e) {
    const timedOut = controller.signal.aborted;
    return NextResponse.json(
      {
        message: timedOut
          ? "The export took too long to start. Try a shorter time window."
          : `Couldn't reach the server for the export: ${(e as Error).message}`,
      },
      { status: timedOut ? 504 : 502 },
    );
  } finally {
    clearTimeout(timer);
  }

  if (!upstream.ok) {
    return NextResponse.json(
      { message: await errorMessageFrom(upstream, `Export failed (${upstream.status})`) },
      { status: upstream.status },
    );
  }

  return new NextResponse(upstream.body, {
    status: 200,
    headers: {
      "Content-Type": upstream.headers.get("content-type") ?? "text/csv; charset=utf-8",
      "Content-Disposition":
        upstream.headers.get("content-disposition") ?? `attachment; filename=${fallbackFilename}`,
    },
  });
}
