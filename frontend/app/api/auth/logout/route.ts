import { NextResponse } from "next/server";
import { clearSessionCookies, isSecureRequest } from "@/lib/auth/session";

export async function POST(req: Request) {
  const res = new NextResponse(null, { status: 204 });
  clearSessionCookies(res.cookies, isSecureRequest(req));
  return res;
}
