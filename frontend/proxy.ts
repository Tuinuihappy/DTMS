import { NextResponse, type NextRequest } from "next/server";
import { decodeJwt, isExpired } from "@/lib/auth/jwt";
import {
  clearSessionCookies,
  isSecureRequest,
  readSessionToken,
} from "@/lib/auth/session";

/* -------------------------------------------------------------------------- */
/* Route protection.                                                          */
/*                                                                            */
/* This is an anti-enumeration + UX guard, NOT an authorization boundary:     */
/* decodeJwt only base64-decodes, it never verifies the RS256 signature. The  */
/* backend re-validates every API call, which is what actually protects data. */
/*                                                                            */
/* The matcher is an explicit prefix allowlist rather than a broad            */
/* negative-lookahead. A forgotten prefix leaves one route group reachable    */
/* while logged out (loud, and caught by scripts/check-route-protection.mjs); */
/* a regex slip silently 307s static assets — which would break PWA install,  */
/* kill the service worker, and, worst, make the offline queue's drainQueue   */
/* treat a redirected-to login page as success and DELETE queued operator     */
/* actions.                                                                   */
/* -------------------------------------------------------------------------- */

// Next matches the proxy against App Router transport URLs too — /x.rsc and
// /x.segments/<id>.segment.rsc — so a raw pathname comparison would miss
// them. Without this, /m/login.rsc doesn't equal "/m/login" and the login
// page redirects to itself forever.
function routePath(pathname: string): string {
  const segments = pathname.indexOf(".segments/");
  let p = segments !== -1 ? pathname.slice(0, segments) : pathname;
  if (p.endsWith(".rsc")) p = p.slice(0, -4);
  if (p.length > 1 && p.endsWith("/")) p = p.slice(0, -1);
  return p || "/";
}

// `_rsc` is Next's cache-busting query param on RSC fetches. Letting it into
// ?from= would make the post-login navigation replay a stale RSC key.
function returnTo(pathname: string, search: string): string {
  const params = new URLSearchParams(search);
  params.delete("_rsc");
  const qs = params.toString();
  return routePath(pathname) + (qs ? `?${qs}` : "");
}

export function proxy(req: NextRequest) {
  const token = readSessionToken((name) => req.cookies.get(name)?.value);
  const claims = token ? decodeJwt(token) : null;
  const valid = claims && !isExpired(claims);

  const route = routePath(req.nextUrl.pathname);
  // The operator PWA has its own login page and its own post-login
  // destination — sending an operator to the desktop /login would drop them
  // on the marketing-styled page with no way back into the app.
  const isOperator = route === "/m" || route.startsWith("/m/");

  if (route === "/m/login") {
    return valid
      ? NextResponse.redirect(new URL("/m/trips", req.url))
      : NextResponse.next();
  }

  if (!valid) {
    const url = req.nextUrl.clone();
    const from = returnTo(req.nextUrl.pathname, req.nextUrl.search);
    url.pathname = isOperator ? "/m/login" : "/login";
    url.search = `?from=${encodeURIComponent(from)}`;
    const res = NextResponse.redirect(url);
    if (token) clearSessionCookies(res.cookies, isSecureRequest(req));
    return res;
  }

  return NextResponse.next();
}

// Must stay a literal array — Next statically extracts this at build time,
// so computed values or imported constants silently produce no matcher.
// Keep in sync with scripts/check-route-protection.mjs (it fails the build
// when an app/ route group appears here neither as a prefix nor as public).
export const config = {
  matcher: [
    "/dashboard/:path*",
    "/profile/:path*",
    "/delivery-orders/:path*",
    "/facility/:path*",
    "/home/:path*",
    "/reports/:path*",
    "/dispatch/:path*",
    "/admin/:path*",
    "/m/:path*",
  ],
};
