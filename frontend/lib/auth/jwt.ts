export type JwtClaims = {
  employeeCode: string;
  role: string;
  username: string;
  exp: number;
};

const ROLE_CLAIM =
  "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";
const NAME_CLAIM =
  "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name";

function base64UrlDecode(input: string): string {
  const padded = input.replace(/-/g, "+").replace(/_/g, "/");
  const pad = padded.length % 4 === 0 ? "" : "=".repeat(4 - (padded.length % 4));
  const b64 = padded + pad;
  if (typeof atob === "function") return atob(b64);
  return Buffer.from(b64, "base64").toString("binary");
}

export function decodeJwt(token: string): JwtClaims | null {
  const parts = token.split(".");
  if (parts.length !== 3) return null;
  try {
    const json = decodeURIComponent(
      Array.from(base64UrlDecode(parts[1]))
        .map((c) => "%" + c.charCodeAt(0).toString(16).padStart(2, "0"))
        .join(""),
    );
    const payload = JSON.parse(json);
    const employeeCode = String(payload.EmployeeId ?? payload.employeeCode ?? "");
    const role = String(payload[ROLE_CLAIM] ?? payload.role ?? "");
    const username = String(payload[NAME_CLAIM] ?? payload.name ?? "");
    const exp = typeof payload.exp === "number" ? payload.exp : 0;
    if (!exp) return null;
    return { employeeCode, role, username, exp };
  } catch {
    return null;
  }
}

export function isExpired(claims: JwtClaims, nowSeconds = Math.floor(Date.now() / 1000)): boolean {
  return claims.exp <= nowSeconds;
}
