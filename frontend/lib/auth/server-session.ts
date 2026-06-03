import "server-only";

import { cookies } from "next/headers";
import { decodeJwt, isExpired, type JwtClaims } from "./jwt";
import { SESSION_COOKIE } from "./session";

export async function getServerSession(): Promise<JwtClaims | null> {
  const jar = await cookies();
  const token = jar.get(SESSION_COOKIE)?.value;
  if (!token) return null;
  const claims = decodeJwt(token);
  if (!claims || isExpired(claims)) return null;
  return claims;
}

export async function getServerToken(): Promise<string | null> {
  const jar = await cookies();
  return jar.get(SESSION_COOKIE)?.value ?? null;
}
