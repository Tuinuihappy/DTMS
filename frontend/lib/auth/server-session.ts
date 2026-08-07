import "server-only";

import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { decodeJwt, isExpired, type JwtClaims } from "./jwt";
import { readSessionToken } from "./session";

export async function getServerSession(): Promise<JwtClaims | null> {
  const token = await getServerToken();
  if (!token) return null;
  const claims = decodeJwt(token);
  if (!claims || isExpired(claims)) return null;
  return claims;
}

export async function getServerToken(): Promise<string | null> {
  const jar = await cookies();
  return readSessionToken((name) => jar.get(name)?.value);
}

/**
 * Server Component page guard: redirect to `loginPath` unless the request
 * carries a live session.
 *
 * Routing through getServerSession is what makes this correct — reading
 * the `dtms_session` cookie directly misses chunked tokens entirely (a
 * >3500-byte JWT is stored as dtms_session.0/.1/…, so `dtms_session` is
 * absent and the guard bounces a freshly signed-in user straight back to
 * the login page), and skips the expiry check.
 *
 * A Server Component cannot call cookies().set(), so this can only
 * redirect — stale cookies are cleared by the proxy on the next request.
 */
export async function requireSession(loginPath: string): Promise<JwtClaims> {
  const claims = await getServerSession();
  if (!claims) redirect(loginPath);
  return claims;
}

/** Inverse guard for login pages: bounce an already-signed-in visitor on. */
export async function redirectIfSignedIn(destination: string): Promise<void> {
  if (await getServerSession()) redirect(destination);
}
