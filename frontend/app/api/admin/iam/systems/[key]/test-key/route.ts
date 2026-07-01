import "server-only";

import { NextResponse, type NextRequest } from "next/server";

// "Test this credential" button on OneTimeSecretBanner posts the freshly-
// minted client_secret here. We trade it for an access token via
// /oauth/token (the exact OAuth flow OMS will run in prod), then call
// /source/{key}/whoami with `Authorization: Bearer <jwt>` to confirm
// end-to-end auth works. A token-endpoint failure is returned as ok:false
// with the upstream's RFC 6749 error body so the operator can distinguish
// "wrong secret" from "wrong target system".
//
// Body shape: { secret: "dtms_cs_<key>_..." }
// Response:   { ok: true, principalId, displayName, permissions }
//             or { ok: false, status, message }

type Ctx = { params: Promise<{ key: string }> };
type TestMode = "client-secret" | "jwt";

export async function POST(req: NextRequest, { params }: Ctx) {
  const { key } = await params;

  let secret: string;
  let mode: TestMode;
  try {
    const body = (await req.json()) as { secret?: string; mode?: string };
    secret = (body.secret ?? "").trim();
    const m = body.mode ?? "client-secret";
    if (m !== "client-secret" && m !== "jwt") {
      return NextResponse.json(
        { ok: false, message: "mode must be 'client-secret' or 'jwt'" },
        { status: 400 },
      );
    }
    mode = m;
  } catch {
    return NextResponse.json(
      { ok: false, message: "Body must be JSON with { secret, mode? }" },
      { status: 400 },
    );
  }
  if (!secret) {
    return NextResponse.json({ ok: false, message: "secret is required" }, { status: 400 });
  }

  const base = process.env.DTMS_BACKEND_URL;
  if (!base) {
    return NextResponse.json(
      { ok: false, message: "Server misconfigured: DTMS_BACKEND_URL is not set" },
      { status: 500 },
    );
  }

  const root = base.replace(/\/$/, "");
  const whoamiUrl = `${root}/api/v1/source/${encodeURIComponent(key)}/whoami`;

  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), 10_000);

  try {
    // jwt mode skips the /oauth/token exchange — the value IS the bearer
    // token. Used after Issue JWT in admin UI: same middleware path the
    // partner will hit, no synthetic OAuth round-trip in between.
    let authHeader: string;
    if (mode === "jwt") {
      authHeader = `Bearer ${secret}`;
    } else {
      const tokenRes = await fetch(`${root}/oauth/token`, {
        method: "POST",
        headers: { "Content-Type": "application/x-www-form-urlencoded" },
        body: new URLSearchParams({
          grant_type: "client_credentials",
          client_id: key,
          client_secret: secret,
        }).toString(),
        cache: "no-store",
        signal: controller.signal,
      });
      const tokenText = await tokenRes.text();
      if (!tokenRes.ok) {
        let message = `Token endpoint returned ${tokenRes.status}`;
        try {
          const errBody = tokenText ? JSON.parse(tokenText) : null;
          if (errBody && typeof errBody === "object") {
            const e = errBody as Record<string, unknown>;
            const desc = (e.error_description ?? e.error) as string | undefined;
            if (desc) message = desc;
          }
        } catch {
          if (tokenText) message = tokenText;
        }
        return NextResponse.json(
          { ok: false, status: tokenRes.status, message },
          { status: 200 },
        );
      }
      let token: string;
      try {
        const parsed = JSON.parse(tokenText) as { access_token?: string };
        token = parsed.access_token ?? "";
      } catch {
        return NextResponse.json(
          { ok: false, message: "Token endpoint returned non-JSON response." },
          { status: 200 },
        );
      }
      if (!token) {
        return NextResponse.json(
          { ok: false, message: "Token endpoint returned no access_token." },
          { status: 200 },
        );
      }
      authHeader = `Bearer ${token}`;
    }

    const upstream = await fetch(whoamiUrl, {
      method: "GET",
      headers: {
        Accept: "application/json",
        Authorization: authHeader,
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
        message: aborted
          ? "Backend didn't respond in time."
          : "Couldn't reach the backend service.",
      },
      { status: 200 },
    );
  } finally {
    clearTimeout(timer);
  }
}
