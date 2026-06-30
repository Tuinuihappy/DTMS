import "server-only";

import { NextResponse, type NextRequest } from "next/server";

// Phase S.6 UX — "Test this key" button on the OneTimeSecretBanner
// posts the freshly-minted plaintext here. We forward it to the
// backend's `/source/{key}/whoami` smoke endpoint using the ApiKey
// scheme (NOT the admin Bearer token) so the verify exercises the
// same code path OMS would in production.
//
// Body shape: { apiKey: "dtms_oms_xxx" } (full plaintext, no "ApiKey " prefix)
// Response:   { ok: true, principalId, displayName, permissions }
//             or { ok: false, status, message }

type Ctx = { params: Promise<{ key: string }> };

export async function POST(req: NextRequest, { params }: Ctx) {
  const { key } = await params;

  let apiKey: string;
  try {
    const body = (await req.json()) as { apiKey?: string };
    apiKey = (body.apiKey ?? "").trim();
  } catch {
    return NextResponse.json({ ok: false, message: "Body must be JSON with { apiKey }" }, { status: 400 });
  }
  if (!apiKey) {
    return NextResponse.json({ ok: false, message: "apiKey is required" }, { status: 400 });
  }

  const base = process.env.DTMS_BACKEND_URL;
  if (!base) {
    return NextResponse.json({ ok: false, message: "Server misconfigured: DTMS_BACKEND_URL is not set" }, { status: 500 });
  }

  const url = `${base.replace(/\/$/, "")}/api/v1/source/${encodeURIComponent(key)}/whoami`;

  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), 10_000);
  try {
    const upstream = await fetch(url, {
      method: "GET",
      headers: {
        Accept: "application/json",
        Authorization: `ApiKey ${apiKey}`,
      },
      cache: "no-store",
      signal: controller.signal,
    });

    const text = await upstream.text();
    if (upstream.ok) {
      let parsed: unknown = null;
      try {
        parsed = text ? JSON.parse(text) : {};
      } catch {
        /* ignore */
      }
      return NextResponse.json({ ok: true, ...((parsed as object) ?? {}) }, { status: 200 });
    }

    let message = `Backend returned ${upstream.status}`;
    try {
      const errBody = text ? JSON.parse(text) : null;
      if (errBody && typeof errBody === "object" && "error" in errBody) {
        message = String((errBody as Record<string, unknown>).error);
      }
    } catch {
      if (text) message = text;
    }

    return NextResponse.json({ ok: false, status: upstream.status, message }, { status: 200 });
  } catch (err) {
    const aborted = (err as { name?: string })?.name === "AbortError";
    return NextResponse.json(
      {
        ok: false,
        message: aborted ? "Backend didn't respond in time." : "Couldn't reach the backend service.",
      },
      { status: 200 },
    );
  } finally {
    clearTimeout(timer);
  }
}
