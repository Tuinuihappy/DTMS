import { NextResponse } from "next/server";
import { SESSION_COOKIE, isSecureRequest } from "@/lib/auth/session";

export async function POST(req: Request) {
  const res = new NextResponse(null, { status: 204 });
  res.cookies.set(SESSION_COOKIE, "", {
    httpOnly: true,
    secure: isSecureRequest(req),
    sameSite: "lax",
    path: "/",
    expires: new Date(0),
  });
  return res;
}
