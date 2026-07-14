// Phase S.6 — IAM admin client for SystemClient + SystemEventSubscription.
// Hits /api/admin/iam/systems/* (Next proxy → /api/v1/iam/systems/*).
// Mirrors the shape of lib/api/iam.ts; kept in its own file so the
// existing IAM permission/role admin client stays focused on its
// surface area.

import type { PermissionDto } from "./iam";

// ── DTOs (mirror SystemAdminEndpoints + SystemSubscriptionEndpoints) ─────

export type SystemSummaryDto = {
  key: string;
  displayName: string;
  description: string | null;
  isActive: boolean;
  ownerContact: string | null;
  createdAt: string;
};

export type CredentialSummary = {
  authScheme: string;
  hasCallbackBaseUrl: boolean;
  callbackBaseUrl: string | null;
  callbackAuthScheme: string | null;
  callbackTimeoutMs: number;
  updatedAt: string;
  // Phase S.6 follow-up — when CallbackAuthScheme=bearer + token is a JWT
  // with an exp claim, backend decodes it. Null otherwise.
  callbackTokenExpiresAt: string | null;
};

export type SubscriptionSummary = {
  eventType: string;
  payloadFormatKey: string;
  enabled: boolean;
};

export type SystemDetailDto = SystemSummaryDto & {
  permissions: string[];
  subscriptions: SubscriptionSummary[];
  credential: CredentialSummary | null;
};

// Returned plaintext exactly once at create/rotate time. authScheme is
// always "bearer-jwt" today (single supported scheme); secret is the
// OAuth client_secret ("dtms_cs_<key>_..."). Partners POST it to
// /oauth/token to receive a short-lived JWT for inbound API calls.
export type CreatedSystemResponse = SystemSummaryDto & {
  permissions: string[];
  authScheme: AuthScheme;
  secret: string;          // ← one-time plaintext, never re-fetchable
};

export type RotateCredentialResponse = {
  secret: string;          // ← one-time plaintext (see CreatedSystemResponse)
  authScheme: AuthScheme;
  rotatedAt: string;
};

// Kept as a union so adding a second scheme later is a one-liner.
// Today: bearer-jwt only.
export type AuthScheme = "bearer-jwt";

export type SubscriptionDto = {
  id: string;
  systemKey: string;
  eventType: string;
  payloadFormatKey: string;
  enabled: boolean;
  createdAtUtc: string;
  updatedAtUtc: string;
};

// ── Request shapes ───────────────────────────────────────────────────────

export type CreateSystemRequest = {
  key: string;
  displayName: string;
  description?: string | null;
  ownerContact?: string | null;
  isActive?: boolean | null;
  // Phase S.8 — omit to default to api-key (backward compat).
  authScheme?: AuthScheme;
};

export type RotateSchemeRequest = {
  authScheme: AuthScheme;
};

export type PatchSystemRequest = {
  displayName?: string | null;
  description?: string | null;
  ownerContact?: string | null;
};

export type CallbackConfigRequest = {
  callbackBaseUrl: string | null;
  callbackAuthScheme: string | null;
  callbackBearerToken: string | null;
  callbackTimeoutMs?: number | null;
  retryMaxAttempts?: number | null;
  circuitFailureThreshold?: number | null;
  circuitDurationSeconds?: number | null;
};

export type CreateSubscriptionRequest = {
  eventType: string;
  payloadFormatKey: string;
  enabled?: boolean | null;
};

export type PatchSubscriptionRequest = {
  enabled?: boolean | null;
  payloadFormatKey?: string | null;
};

// ── Systems CRUD ─────────────────────────────────────────────────────────

export async function listSystems(signal?: AbortSignal): Promise<SystemSummaryDto[]> {
  const res = await fetch("/api/admin/iam/systems", { signal, cache: "no-store" });
  if (!res.ok) throw new Error(`Failed to load systems: ${res.status}`);
  return (await res.json()) as SystemSummaryDto[];
}

export async function getSystem(
  key: string,
  signal?: AbortSignal,
): Promise<SystemDetailDto> {
  const res = await fetch(`/api/admin/iam/systems/${encodeURIComponent(key)}`, {
    signal,
    cache: "no-store",
  });
  if (!res.ok) throw new Error(await readError(res));
  return (await res.json()) as SystemDetailDto;
}

// The source-system permission templates this system can hold (order + trip),
// resolved for its key. Served from the backend (StandardSystemPermissions.All)
// so the "Source" grant checklist never drifts from what the backend enforces.
export async function listStandardSystemPermissions(
  key: string,
  signal?: AbortSignal,
): Promise<PermissionDto[]> {
  const res = await fetch(
    `/api/admin/iam/systems/${encodeURIComponent(key)}/permissions/standard`,
    { signal, cache: "no-store" },
  );
  if (!res.ok) throw new Error(`Failed to load standard permissions: ${res.status}`);
  return (await res.json()) as PermissionDto[];
}

export async function createSystem(body: CreateSystemRequest): Promise<CreatedSystemResponse> {
  const res = await fetch("/api/admin/iam/systems", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
  if (!res.ok) throw new Error(await readError(res));
  return (await res.json()) as CreatedSystemResponse;
}

export async function patchSystem(
  key: string,
  body: PatchSystemRequest,
): Promise<SystemSummaryDto> {
  const res = await fetch(`/api/admin/iam/systems/${encodeURIComponent(key)}`, {
    method: "PATCH",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
  if (!res.ok) throw new Error(await readError(res));
  return (await res.json()) as SystemSummaryDto;
}

export async function activateSystem(key: string): Promise<void> {
  const res = await fetch(`/api/admin/iam/systems/${encodeURIComponent(key)}/activate`, {
    method: "POST",
  });
  if (!res.ok) throw new Error(await readError(res));
}

export async function deactivateSystem(key: string): Promise<void> {
  const res = await fetch(`/api/admin/iam/systems/${encodeURIComponent(key)}/deactivate`, {
    method: "POST",
  });
  if (!res.ok) throw new Error(await readError(res));
}

export async function setCallback(
  key: string,
  body: CallbackConfigRequest,
): Promise<void> {
  const res = await fetch(`/api/admin/iam/systems/${encodeURIComponent(key)}/callback`, {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
  if (!res.ok) throw new Error(await readError(res));
}

export async function deleteSystem(key: string): Promise<void> {
  const res = await fetch(`/api/admin/iam/systems/${encodeURIComponent(key)}`, {
    method: "DELETE",
  });
  if (!res.ok) throw new Error(await readError(res));
}

export async function rotateCredential(key: string): Promise<RotateCredentialResponse> {
  const res = await fetch(
    `/api/admin/iam/systems/${encodeURIComponent(key)}/credential/rotate`,
    { method: "POST" },
  );
  if (!res.ok) throw new Error(await readError(res));
  return (await res.json()) as RotateCredentialResponse;
}

// Phase S.8 — change the credential's auth scheme (api-key ↔ bearer-jwt).
// Mints a fresh secret of the new scheme; cache invalidated server-side so
// the partner must update their integration before the next call.
export async function rotateScheme(
  key: string,
  body: RotateSchemeRequest,
): Promise<RotateCredentialResponse> {
  const res = await fetch(
    `/api/admin/iam/systems/${encodeURIComponent(key)}/credential/scheme`,
    {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
    },
  );
  if (!res.ok) throw new Error(await readError(res));
  return (await res.json()) as RotateCredentialResponse;
}

// Admin-issued long-lived JWT (escape hatch for partners that can't run an
// OAuth client). Hands back a JWT the partner sends as Authorization:
// Bearer <jwt> directly — no /oauth/token round-trip on their side. Default
// lifetime is 90 days; bounded at the endpoint to [60s, 365d].
// Phase S.8d — neverExpires mints a perpetual token (no exp claim). Set
// EITHER lifetimeSeconds OR neverExpires, never both (backend 400s on both).
export type IssueTokenRequest = { lifetimeSeconds?: number; neverExpires?: boolean };
export type IssueTokenResponse = {
  accessToken: string;
  tokenType: string;
  // Null for a perpetual token — no exp, so no expires_in / absolute expiry.
  expiresInSeconds: number | null;
  expiresAt: string | null;
};

export async function issueToken(
  key: string,
  body: IssueTokenRequest,
): Promise<IssueTokenResponse> {
  const res = await fetch(
    `/api/admin/iam/systems/${encodeURIComponent(key)}/issue-token`,
    {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
    },
  );
  if (!res.ok) throw new Error(await readError(res));
  return (await res.json()) as IssueTokenResponse;
}

// Phase S.8c — audit + revocation list for admin-issued long-lived JWTs.
// One row per Issue click; status flips Active → Revoked on revoke.
export type IssuedTokenSummary = {
  jti: string;
  issuedAt: string;
  // Null for a perpetual token (Phase S.8d) — rendered as "Never".
  expiresAt: string | null;
  issuedBy: string;
  status: "Active" | "Revoked";
  revokedAt: string | null;
  revokedBy: string | null;
  revokeReason: string | null;
};

export async function listIssuedTokens(
  key: string,
  signal?: AbortSignal,
): Promise<IssuedTokenSummary[]> {
  const res = await fetch(
    `/api/admin/iam/systems/${encodeURIComponent(key)}/tokens`,
    { signal, cache: "no-store" },
  );
  if (!res.ok) throw new Error(await readError(res));
  return (await res.json()) as IssuedTokenSummary[];
}

export async function revokeToken(
  key: string,
  jti: string,
  reason?: string,
): Promise<void> {
  const res = await fetch(
    `/api/admin/iam/systems/${encodeURIComponent(key)}/tokens/${encodeURIComponent(jti)}/revoke`,
    {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(reason ? { reason } : {}),
    },
  );
  if (!res.ok) throw new Error(await readError(res));
}

// ── Subscriptions CRUD ───────────────────────────────────────────────────

export async function getEventTypes(signal?: AbortSignal): Promise<string[]> {
  const res = await fetch("/api/admin/iam/event-types", { signal, cache: "no-store" });
  if (!res.ok) throw new Error(`Failed to load event types: ${res.status}`);
  return (await res.json()) as string[];
}

export async function listSubscriptions(
  key: string,
  signal?: AbortSignal,
): Promise<SubscriptionDto[]> {
  const res = await fetch(
    `/api/admin/iam/systems/${encodeURIComponent(key)}/subscriptions`,
    { signal, cache: "no-store" },
  );
  if (!res.ok) throw new Error(await readError(res));
  return (await res.json()) as SubscriptionDto[];
}

export async function createSubscription(
  key: string,
  body: CreateSubscriptionRequest,
): Promise<SubscriptionDto> {
  const res = await fetch(
    `/api/admin/iam/systems/${encodeURIComponent(key)}/subscriptions`,
    {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
    },
  );
  if (!res.ok) throw new Error(await readError(res));
  return (await res.json()) as SubscriptionDto;
}

export async function patchSubscription(
  key: string,
  eventType: string,
  body: PatchSubscriptionRequest,
): Promise<SubscriptionDto> {
  const res = await fetch(
    `/api/admin/iam/systems/${encodeURIComponent(key)}/subscriptions/${encodeURIComponent(eventType)}`,
    {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
    },
  );
  if (!res.ok) throw new Error(await readError(res));
  return (await res.json()) as SubscriptionDto;
}

export async function deleteSubscription(key: string, eventType: string): Promise<void> {
  const res = await fetch(
    `/api/admin/iam/systems/${encodeURIComponent(key)}/subscriptions/${encodeURIComponent(eventType)}`,
    { method: "DELETE" },
  );
  if (!res.ok) throw new Error(await readError(res));
}

// ── Principal self-introspection (Phase S.6) ─────────────────────────────

export type PrincipalPermissionsDto = { permissions: string[] };

export async function getMyPermissions(signal?: AbortSignal): Promise<string[]> {
  const res = await fetch("/api/admin/iam/me/permissions", { signal, cache: "no-store" });
  if (!res.ok) throw new Error(`Failed to load principal permissions: ${res.status}`);
  const data = (await res.json()) as PrincipalPermissionsDto;
  return data.permissions ?? [];
}

// ── Test credential (Phase S.6 UX — verify a freshly-minted secret works) ─

export type TestCredentialResult =
  | { ok: true; principalId?: string; displayName?: string; permissions?: string[] }
  | { ok: false; status?: number; message?: string };

// "client-secret" → handler trades secret for a JWT via /oauth/token first.
// "jwt"           → handler uses the value as Bearer directly (for admin-
//                   issued long-lived JWTs that skip /oauth/token entirely).
export type TestCredentialMode = "client-secret" | "jwt";

/**
 * Probe the backend to confirm a freshly-minted credential authenticates
 * end-to-end. For client-secret mode the route handler runs the full OAuth
 * flow (exchange → use JWT to call /whoami) — exactly what OMS does in
 * prod. For jwt mode the handler sends `Authorization: Bearer <value>`
 * directly, exercising the same middleware path partners hit with admin-
 * issued tokens. Plaintext stays server-side either way (the proxy handler
 * holds it), so it never lands in browser network logs.
 */
export async function testCredential(
  key: string,
  secret: string,
  mode: TestCredentialMode = "client-secret",
): Promise<TestCredentialResult> {
  const res = await fetch(`/api/admin/iam/systems/${encodeURIComponent(key)}/test-key`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ secret, mode }),
  });
  // Route handler always returns 200 with an `ok` field — even on
  // upstream failures — so we can render the result inline without
  // dealing with thrown exceptions for expected "secret didn't work" cases.
  return (await res.json()) as TestCredentialResult;
}

// ── permission grant / revoke (Phase S.7) ────────────────────────────────

export async function grantSystemPermission(key: string, code: string): Promise<void> {
  const res = await fetch(
    `/api/admin/iam/systems/${encodeURIComponent(key)}/permissions/${encodeURIComponent(code)}`,
    { method: "POST" },
  );
  if (!res.ok) throw new Error(await readError(res));
}

export async function revokeSystemPermission(key: string, code: string): Promise<void> {
  const res = await fetch(
    `/api/admin/iam/systems/${encodeURIComponent(key)}/permissions/${encodeURIComponent(code)}`,
    { method: "DELETE" },
  );
  if (!res.ok) throw new Error(await readError(res));
}

// ── helpers ──────────────────────────────────────────────────────────────

// Duplicated from iam.ts (private there). Could be lifted to a shared
// module later — for now keeping it local to avoid breaking the existing
// file's surface.
async function readError(res: Response): Promise<string> {
  try {
    const body = (await res.json()) as { error?: string; message?: string };
    return body.error ?? body.message ?? `Request failed: ${res.status}`;
  } catch {
    return `Request failed: ${res.status}`;
  }
}
