import { NextResponse } from "next/server";
import { decodeJwt } from "@/lib/auth/jwt";
import {
  SESSION_COOKIE,
  isSecureRequest,
  type AuthUser,
  type LoginResponse,
} from "@/lib/auth/session";

export async function POST(req: Request) {
  const apiBase = process.env.DTMS_API_BASE_URL;
  if (!apiBase) {
    return NextResponse.json(
      { message: "Server misconfigured: DTMS_API_BASE_URL is not set." },
      { status: 500 },
    );
  }

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
