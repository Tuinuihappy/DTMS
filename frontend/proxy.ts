import { NextResponse, type NextRequest } from "next/server";
import { decodeJwt, isExpired } from "@/lib/auth/jwt";
import { SESSION_COOKIE, isSecureRequest } from "@/lib/auth/session";

export function proxy(req: NextRequest) {
  const token = req.cookies.get(SESSION_COOKIE)?.value;
  const claims = token ? decodeJwt(token) : null;
  const valid = claims && !isExpired(claims);

  if (!valid) {
    const url = req.nextUrl.clone();
    const from = req.nextUrl.pathname + req.nextUrl.search;
    url.pathname = "/login";
    url.search = `?from=${encodeURIComponent(from)}`;
    const res = NextResponse.redirect(url);
    if (token) {
      res.cookies.set(SESSION_COOKIE, "", {
        httpOnly: true,
        secure: isSecureRequest(req),
        sameSite: "lax",
        path: "/",
        expires: new Date(0),
      });
    }
    return res;
  }

  return NextResponse.next();
}

export const config = {
  matcher: ["/dashboard/:path*", "/profile/:path*", "/delivery-orders/:path*"],
};
