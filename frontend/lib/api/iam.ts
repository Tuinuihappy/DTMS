// IAM admin client — hits /api/admin/iam/* (Next proxy → /api/v1/iam/*).
// Backend permission system is described in src/Modules/Iam (Phase A + B).
// Wildcards like "dtms:*" are valid grants (matched at the auth handler)
// but won't appear in the Permissions catalog — the matrix view resolves
// them locally.

export type PermissionDto = {
  code: string;
  description: string;
  module: string;
};

export type AuditLogEntryDto = {
  id: string;
  occurredAt: string;
  actorEmployeeId: string;
  // system-* / subscription-* today; grant / revoke / role-* in rows from
  // before roles were removed (2026-08-01). The live list comes from
  // listAuditActions().
  action: string;
  role: string | null;
  permissionCode: string | null;
  // Compact JSON; system-* and subscription-* rows carry "systemKey".
  details: string | null;
};

export type AuditLogPage = {
  items: AuditLogEntryDto[];
  totalCount: number;
  page: number;
  pageSize: number;
};

// ── Permissions ──────────────────────────────────────────────────────────

// The catalog is defined in code and served read-only — there is no
// create/update/delete. Add or rename a permission via a code change + deploy.
export async function listPermissions(signal?: AbortSignal): Promise<PermissionDto[]> {
  const res = await fetch("/api/admin/iam/permissions", { signal, cache: "no-store" });
  if (!res.ok) throw new Error(`Failed to load permissions: ${res.status}`);
  return (await res.json()) as PermissionDto[];
}

// ── Audit log ────────────────────────────────────────────────────────────

export async function queryAuditLog(params: {
  actor?: string;
  role?: string;
  action?: string;
  systemKey?: string;
  page?: number;
  pageSize?: number;
  signal?: AbortSignal;
}): Promise<AuditLogPage> {
  const search = new URLSearchParams();
  if (params.actor) search.set("actor", params.actor);
  if (params.role) search.set("role", params.role);
  if (params.action) search.set("action", params.action);
  if (params.systemKey) search.set("systemKey", params.systemKey);
  if (params.page) search.set("page", String(params.page));
  if (params.pageSize) search.set("pageSize", String(params.pageSize));

  const qs = search.toString();
  const url = `/api/admin/iam/audit-log${qs ? `?${qs}` : ""}`;
  const res = await fetch(url, { signal: params.signal, cache: "no-store" });
  if (!res.ok) throw new Error(`Failed to load audit log: ${res.status}`);
  return (await res.json()) as AuditLogPage;
}

/** Every action that occurs in the audit log, A→Z. */
export async function listAuditActions(signal?: AbortSignal): Promise<string[]> {
  const res = await fetch("/api/admin/iam/audit-log/actions", { signal, cache: "no-store" });
  if (!res.ok) throw new Error(`Failed to load audit actions: ${res.status}`);
  return (await res.json()) as string[];
}

// ── Helpers ──────────────────────────────────────────────────────────────

// True when `held` (a granted permission code) covers `required` (the
// permission a row in the matrix represents). Mirrors the backend's
// PermissionAuthorizationHandler.Matches so the UI can render
// wildcard-covered cells without a server round-trip.
export function matches(held: string, required: string): boolean {
  if (held === required) return true;
  if (!held.endsWith(":*")) return false;
  const prefix = held.slice(0, -1); // keep trailing ':'
  return required.startsWith(prefix);
}

