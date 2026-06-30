// Phase S.6 — IAM admin client for SystemClient + SystemEventSubscription.
// Hits /api/admin/iam/systems/* (Next proxy → /api/v1/iam/systems/*).
// Mirrors the shape of lib/api/iam.ts; kept in its own file so the
// existing IAM permission/role admin client stays focused on its
// surface area.

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

export type CreatedSystemResponse = SystemSummaryDto & {
  permissions: string[];
  apiKey: string;          // ← one-time plaintext, never re-fetchable
};

export type RotateCredentialResponse = {
  apiKey: string;          // ← one-time plaintext
  rotatedAt: string;
};

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

export async function rotateCredential(key: string): Promise<RotateCredentialResponse> {
  const res = await fetch(
    `/api/admin/iam/systems/${encodeURIComponent(key)}/credential/rotate`,
    { method: "POST" },
  );
  if (!res.ok) throw new Error(await readError(res));
  return (await res.json()) as RotateCredentialResponse;
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
