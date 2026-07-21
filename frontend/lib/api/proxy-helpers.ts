import "server-only";

import { NextResponse, type NextRequest } from "next/server";
import { getServerToken } from "@/lib/auth/server-session";

export type ProxyOptions = {
  method?: "GET" | "POST" | "PUT" | "DELETE" | "PATCH";
  path: string;
  search?: URLSearchParams | string;
  body?: unknown;
  timeoutMs?: number;
  // Inbound request — used to forward client-supplied headers like
  // Idempotency-Key. Optional so GET endpoints that don't need any
  // pass-through can omit it.
  inbound?: NextRequest | Request;
  // When true, the upstream response is returned verbatim (status + body).
  // When false (default), non-2xx responses are normalized into a JSON
  // { message } payload at the same status.
  passthroughErrors?: boolean;
};

// Headers we relay from the inbound Next.js request to the upstream
// backend. Kept narrow: only safe, client-supplied request hints. Note
// Authorization is sourced from the cookie (not from inbound headers),
// so it isn't listed here.
const FORWARD_HEADERS = ["idempotency-key"];

const DEFAULT_TIMEOUT = 20_000;

export async function proxyToBackend({
  method = "GET",
  path,
  search,
  body,
  timeoutMs = DEFAULT_TIMEOUT,
  inbound,
  passthroughErrors = false,
}: ProxyOptions): Promise<NextResponse> {
  const base = process.env.DTMS_BACKEND_URL;
  if (!base) {
    return NextResponse.json(
      { message: "Server misconfigured: DTMS_BACKEND_URL is not set." },
      { status: 500 },
    );
  }

  const token = await getServerToken();

  const qs =
    search instanceof URLSearchParams
      ? search.toString()
      : typeof search === "string"
        ? search
        : "";
  const url = `${base.replace(/\/$/, "")}${path}${qs ? `?${qs}` : ""}`;

  const headers: Record<string, string> = {
    Accept: "application/json",
  };
  if (token) headers.Authorization = `Bearer ${token}`;
  if (body !== undefined) headers["Content-Type"] = "application/json";

  // Relay whitelisted client-supplied headers (e.g. Idempotency-Key).
  // Skipped when inbound is omitted — most GET endpoints don't need this.
  if (inbound) {
    for (const name of FORWARD_HEADERS) {
      const value = inbound.headers.get(name);
      if (value) headers[name] = value;
    }
  }

  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeoutMs);

  const t0 = Date.now();
  let upstream: Response;
  try {
    upstream = await fetch(url, {
      method,
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
      cache: "no-store",
      signal: controller.signal,
    });
  } catch (err) {
    const aborted = (err as { name?: string })?.name === "AbortError";
    console.error(
      `[backend-proxy] ${method} ${url} ${aborted ? "TIMEOUT" : "ERROR"} after ${
        Date.now() - t0
      }ms:`,
      err,
    );
    return NextResponse.json(
      {
        message: aborted
          ? "Backend didn't respond in time."
          : "Couldn't reach the backend service.",
      },
      { status: aborted ? 504 : 502 },
    );
  } finally {
    clearTimeout(timer);
  }

  console.log(
    `[backend-proxy] ${method} ${url} → ${upstream.status} in ${Date.now() - t0}ms`,
  );

  // 204 No Content
  if (upstream.status === 204) {
    return new NextResponse(null, { status: 204 });
  }

  // Body may not exist, may be JSON, or may be a plain string error.
  const text = await upstream.text();
  let payload: unknown = null;
  if (text) {
    try {
      payload = JSON.parse(text);
    } catch {
      payload = text;
    }
  }

  if (!upstream.ok && !passthroughErrors) {
    // Backend Result failures come back as { Error: "..." } (see
    // OperatorEndpoints.ToHttp / minimal-API convention), while other
    // handlers use { message: "..." }. Honour both so the real reason
    // (e.g. GEOFENCE_REJECTED) reaches the client instead of being
    // masked by the generic "Upstream error 400" fallback.
    const fromField = (key: string): string | "" => {
      if (typeof payload === "object" && payload !== null) {
        const v = (payload as Record<string, unknown>)[key];
        if (typeof v === "string") return v;
      }
      return "";
    };
    // A raw string body on a 5xx is an unhandled backend error (HTML error
    // page, stack text) — never forward it to the client. 4xx string bodies
    // are intentional Result failures (e.g. GEOFENCE_REJECTED) and stay.
    const rawString =
      typeof payload === "string" && upstream.status < 500 ? payload : "";
    const message =
      fromField("message") ||
      fromField("Error") ||
      fromField("error") ||
      rawString ||
      (upstream.status >= 500
        ? "Server error"
        : `Upstream error ${upstream.status}`);
    // Preserve the backend correlation id (ProblemDetails.traceId) so a user
    // reporting a server error can quote it and support can find the log line.
    const traceId = fromField("traceId");
    return NextResponse.json(
      traceId ? { message, traceId } : { message },
      { status: upstream.status },
    );
  }

  if (payload === null) {
    return new NextResponse(null, { status: upstream.status });
  }
  return NextResponse.json(payload, { status: upstream.status });
}
