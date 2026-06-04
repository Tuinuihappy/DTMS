import { createHmac } from "node:crypto";
import { NextResponse } from "next/server";
import { decodeJwt } from "@/lib/auth/jwt";
import {
  SESSION_COOKIE,
  isSecureRequest,
  type AuthUser,
  type LoginResponse,
} from "@/lib/auth/session";

function base64Url(buf: Buffer): string {
  return buf.toString("base64").replace(/=+$/, "").replace(/\+/g, "-").replace(/\//g, "_");
}

function signDevJwt(claims: Record<string, unknown>, secret: string): string {
  const header = { alg: "HS256", typ: "JWT" };
  const headerSeg = base64Url(Buffer.from(JSON.stringify(header)));
  const payloadSeg = base64Url(Buffer.from(JSON.stringify(claims)));
  const signingInput = `${headerSeg}.${payloadSeg}`;
  const sig = createHmac("sha256", secret).update(signingInput).digest();
  return `${signingInput}.${base64Url(sig)}`;
}

export async function POST(req: Request) {
  let body: { username?: unknown; password?: unknown };
  try {
    body = await req.json();
  } catch {
    return NextResponse.json({ message: "Invalid request body." }, { status: 400 });
  }

  const username = typeof body.username === "string" ? body.username.trim() : "";
  const password = typeof body.password === "string" ? body.password : "";
  if (!username || !password) {
    return NextResponse.json(
      { message: "Username and password are required." },
      { status: 400 },
    );
  }

  // Dev-only short-circuit: when the external auth service is unreachable
  // (e.g. local docker without VPN), set DTMS_AUTH_BYPASS=true to issue a
  // self-signed JWT for whatever username was typed. Backend honors this
  // because Auth__Disable=true uses DevAuthenticationHandler and ignores
  // the bearer signature in Development.
  if (process.env.DTMS_AUTH_BYPASS === "true") {
    const secret = process.env.DTMS_JWT_SECRET ?? "dev-only-secret-min-32-chars-placeholder!";
    const nowSec = Math.floor(Date.now() / 1000);
    const expSec = nowSec + 24 * 60 * 60;
    const token = signDevJwt(
      {
        EmployeeId: username,
        "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name": username,
        "http://schemas.microsoft.com/ws/2008/06/identity/claims/role": "Admin",
        iat: nowSec,
        exp: expSec,
      },
      secret,
    );
    const user: AuthUser = {
      employeeCode: username,
      displayName: username,
      role: "Admin",
      thumbnailPhoto: "",
    };
    const res = NextResponse.json({ user });
    res.cookies.set(SESSION_COOKIE, token, {
      httpOnly: true,
      secure: isSecureRequest(req),
      sameSite: "lax",
      path: "/",
      expires: new Date(expSec * 1000),
    });
    return res;
  }

  const apiBase = process.env.DTMS_API_BASE_URL;
  if (!apiBase) {
    return NextResponse.json(
      { message: "Server misconfigured: DTMS_API_BASE_URL is not set." },
      { status: 500 },
    );
  }

  const t0 = Date.now();
  console.log(`[auth/login] → ${apiBase}/auth/login (user=${username})`);

  let upstream: Response;
  const controller = new AbortController();
  const timeoutId = setTimeout(() => controller.abort(), 15_000);
  try {
    upstream = await fetch(`${apiBase}/auth/login`, {
      method: "POST",
      headers: { "Content-Type": "application/json", Accept: "application/json" },
      body: JSON.stringify({ username, password }),
      cache: "no-store",
      signal: controller.signal,
    });
  } catch (err) {
    const aborted = (err as { name?: string })?.name === "AbortError";
    console.error(
      `[auth/login] upstream ${aborted ? "TIMEOUT" : "ERROR"} after ${Date.now() - t0}ms:`,
      err,
    );
    return NextResponse.json(
      {
        message: aborted
          ? "Auth service didn't respond in 15s. Check the backend or VPN."
          : "Couldn't reach the authentication service. Check your network.",
      },
      { status: aborted ? 504 : 502 },
    );
  } finally {
    clearTimeout(timeoutId);
  }

  console.log(
    `[auth/login] upstream ${upstream.status} in ${Date.now() - t0}ms (user=${username})`,
  );

  if (!upstream.ok) {
    const fallback =
      upstream.status === 401
        ? "Wrong username or password."
        : "Sign-in failed. Please try again.";
    const text = await upstream.text();
    let message = fallback;
    try {
      const parsed = JSON.parse(text);
      if (typeof parsed?.message === "string") message = parsed.message;
    } catch {
      if (text && text.length < 200) message = text;
    }
    return NextResponse.json({ message }, { status: upstream.status });
  }

  const payload = (await upstream.json()) as Partial<LoginResponse>;
  const token = payload.token;
  if (!token) {
    return NextResponse.json(
      { message: "Auth service returned no token." },
      { status: 502 },
    );
  }

  const claims = decodeJwt(token);
  if (!claims) {
    return NextResponse.json(
      { message: "Auth service returned an unreadable token." },
      { status: 502 },
    );
  }

  const user: AuthUser = {
    employeeCode: payload.employeeCode ?? claims.employeeCode,
    displayName: payload.displayName ?? claims.username,
    role: payload.role ?? claims.role,
    thumbnailPhoto: payload.thumbnailPhoto ?? "",
  };

  const res = NextResponse.json({ user });
  res.cookies.set(SESSION_COOKIE, token, {
    httpOnly: true,
    secure: isSecureRequest(req),
    sameSite: "lax",
    path: "/",
    expires: new Date(claims.exp * 1000),
  });
  return res;
}
