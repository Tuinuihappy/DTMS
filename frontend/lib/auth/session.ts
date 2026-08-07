export const SESSION_COOKIE = "dtms_session";
export const PROFILE_STORAGE_KEY = "dtms_profile";

export type AuthUser = {
  employeeCode: string;
  displayName: string;
  role: string;
  thumbnailPhoto: string;
};

export type LoginRequest = {
  username: string;
  password: string;
};

export type LoginResponse = AuthUser & { token: string };

// Browsers reject `Secure` cookies on plain HTTP (except localhost). When
// we serve over HTTP on a LAN IP — common for internal tools and tablet
// access — the cookie would silently drop, leaving the user stuck on
// /login. Detect the actual scheme of THIS request and only mark Secure
// when we're truly on HTTPS. Honors x-forwarded-proto so a TLS-
// terminating reverse proxy in production still produces Secure cookies.
export function isSecureRequest(req: { headers: Headers; url: string }): boolean {
  const fwd = req.headers.get("x-forwarded-proto");
  if (fwd) return fwd.split(",")[0].trim() === "https";
  try {
    return new URL(req.url).protocol === "https:";
  } catch {
    return false;
  }
}

/**
 * Validate a `?from=` value before navigating to it after sign-in.
 *
 * Without this, `/login?from=//evil.com` sends the browser off-origin —
 * router.push treats a protocol-relative path as an absolute URL. Anything
 * that isn't a plain same-origin path falls back, and callers may demand a
 * prefix so the operator PWA can't be used to bounce into the desktop app.
 */
export function sanitizeReturnPath(
  from: string | null | undefined,
  fallback: string,
  requiredPrefix?: string,
): string {
  if (!from) return fallback;
  // Must be a rooted path, and must not be protocol-relative ("//host") or
  // the backslash variant browsers also normalize to one ("/\host").
  if (!from.startsWith("/")) return fallback;
  if (from.startsWith("//") || from.startsWith("/\\")) return fallback;
  if (requiredPrefix && !from.startsWith(requiredPrefix)) return fallback;
  return from;
}

/* -------------------------------------------------------------------------- */
/* Oversized-token chunking                                                    */
/*                                                                            */
/* Browsers silently reject any cookie whose name+value exceeds ~4096 bytes.  */
/* The LDAP auth service embeds the user's entire permission list in the JWT, */
/* which pushes admin tokens past that cap: the Set-Cookie is dropped, the    */
/* proxy sees no session, and login bounces straight back to /login. Tokens   */
/* that don't fit in one cookie are split across numbered cookies             */
/* (dtms_session.0, .1, …) and reassembled on read. Readers accept both the   */
/* single-cookie and the chunked layout.                                      */
/* -------------------------------------------------------------------------- */

// Leaves headroom for the cookie name and attributes inside the 4096-byte
// per-cookie budget browsers enforce.
const SESSION_CHUNK_SIZE = 3500;
// Only bounds clearing (which needs a fixed name list), not reading or
// writing. 8 chunks = 28KB of token, far beyond anything the auth service
// issues today.
const SESSION_MAX_CHUNKS = 8;

const SESSION_COOKIE_NAMES: readonly string[] = [
  SESSION_COOKIE,
  ...Array.from({ length: SESSION_MAX_CHUNKS }, (_, i) => `${SESSION_COOKIE}.${i}`),
];

// Minimal structural view of RequestCookies/ResponseCookies so this module
// stays free of next/server imports (it is also bundled client-side for
// PROFILE_STORAGE_KEY).
type SessionCookieJar = {
  set(
    name: string,
    value: string,
    options?: {
      httpOnly?: boolean;
      secure?: boolean;
      sameSite?: "lax";
      path?: string;
      expires?: Date;
    },
  ): unknown;
};

export function readSessionToken(
  get: (name: string) => string | undefined,
): string | null {
  const single = get(SESSION_COOKIE);
  if (single) return single;
  let token = "";
  for (let i = 0; ; i++) {
    const part = get(`${SESSION_COOKIE}.${i}`);
    if (!part) break;
    token += part;
  }
  return token.length > 0 ? token : null;
}

export function writeSessionCookies(
  jar: SessionCookieJar,
  token: string,
  { secure, expires }: { secure: boolean; expires: Date },
): void {
  // Expire every session cookie name first so a fresh login can never mix
  // with chunks left over from a previous, differently-sized token. The
  // jar dedupes by name, so names re-set below simply win.
  clearSessionCookies(jar, secure);
  const base = { httpOnly: true, secure, sameSite: "lax" as const, path: "/", expires };
  if (token.length <= SESSION_CHUNK_SIZE) {
    jar.set(SESSION_COOKIE, token, base);
    return;
  }
  for (let i = 0; i * SESSION_CHUNK_SIZE < token.length; i++) {
    jar.set(
      `${SESSION_COOKIE}.${i}`,
      token.slice(i * SESSION_CHUNK_SIZE, (i + 1) * SESSION_CHUNK_SIZE),
      base,
    );
  }
}

export function clearSessionCookies(jar: SessionCookieJar, secure: boolean): void {
  for (const name of SESSION_COOKIE_NAMES) {
    jar.set(name, "", {
      httpOnly: true,
      secure,
      sameSite: "lax",
      path: "/",
      expires: new Date(0),
    });
  }
}
