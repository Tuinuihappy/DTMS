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
