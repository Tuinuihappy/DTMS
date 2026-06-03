import { NextResponse } from "next/server";
import { decodeJwt, isExpired } from "@/lib/auth/jwt";
import { getServerToken } from "@/lib/auth/server-session";

export type ProfileResponse = {
  employeeId: string;
  displayName: string;
  email: string;
  thumbnailPhoto: string;
  roles: string[];
};

export async function GET() {
  const apiBase = process.env.DTMS_API_BASE_URL;
  if (!apiBase) {
    return NextResponse.json(
      { message: "Server misconfigured: DTMS_API_BASE_URL is not set." },
      { status: 500 },
    );
  }

  const token = await getServerToken();
  if (!token) {
    return NextResponse.json({ message: "Not authenticated." }, { status: 401 });
  }
  const claims = decodeJwt(token);
  if (!claims || isExpired(claims)) {
    return NextResponse.json({ message: "Session expired." }, { status: 401 });
  }

  const t0 = Date.now();
  const url = `${apiBase}/users/${encodeURIComponent(claims.employeeCode)}`;
  console.log(`[profile] → GET ${url}`);

  let upstream: Response;
  const controller = new AbortController();
  const timeoutId = setTimeout(() => controller.abort(), 12_000);
  try {
    upstream = await fetch(url, {
      method: "GET",
      headers: {
        Accept: "application/json",
        Authorization: `Bearer ${token}`,
      },
      cache: "no-store",
      signal: controller.signal,
    });
  } catch (err) {
    const aborted = (err as { name?: string })?.name === "AbortError";
    console.error(
      `[profile] upstream ${aborted ? "TIMEOUT" : "ERROR"} after ${Date.now() - t0}ms:`,
      err,
    );
    return NextResponse.json(
      {
        message: aborted
          ? "Profile service didn't respond in 12s."
          : "Couldn't reach the profile service.",
      },
      { status: aborted ? 504 : 502 },
    );
  } finally {
    clearTimeout(timeoutId);
  }

  console.log(`[profile] upstream ${upstream.status} in ${Date.now() - t0}ms`);

  if (!upstream.ok) {
    const text = await upstream.text();
    let message = "Profile lookup failed.";
    try {
      const parsed = JSON.parse(text);
      if (typeof parsed?.message === "string") message = parsed.message;
    } catch {
      if (text && text.length < 200) message = text;
    }
    return NextResponse.json({ message }, { status: upstream.status });
  }

  const payload = (await upstream.json()) as Partial<ProfileResponse>;
  const profile: ProfileResponse = {
    employeeId: payload.employeeId ?? claims.employeeCode,
    displayName: payload.displayName ?? claims.username,
    email: payload.email ?? "",
    thumbnailPhoto: payload.thumbnailPhoto ?? "",
    roles: Array.isArray(payload.roles) ? payload.roles : claims.role ? [claims.role] : [],
  };

  return NextResponse.json(profile, {
    headers: { "Cache-Control": "private, no-store" },
  });
}
