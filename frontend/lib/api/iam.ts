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

export type RoleDto = {
  name: string;
  description: string;
  isSystem: boolean;
};

export type AuditLogEntryDto = {
  id: string;
  occurredAt: string;
  actorEmployeeId: string;
  action:
    | "grant"
    | "revoke"
    | "permission-created"
    | "permission-updated"
    | "permission-deleted"
    | "role-created"
    | "role-deleted"
    | string;
  role: string | null;
  permissionCode: string | null;
  details: string | null;
};

export type AuditLogPage = {
  items: AuditLogEntryDto[];
  totalCount: number;
  page: number;
  pageSize: number;
};

// ── Permissions ──────────────────────────────────────────────────────────

export async function listPermissions(signal?: AbortSignal): Promise<PermissionDto[]> {
  const res = await fetch("/api/admin/iam/permissions", { signal, cache: "no-store" });
  if (!res.ok) throw new Error(`Failed to load permissions: ${res.status}`);
  return (await res.json()) as PermissionDto[];
}

export async function createPermission(body: {
  code: string;
  description: string;
  module: string;
}): Promise<PermissionDto> {
  const res = await fetch("/api/admin/iam/permissions", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
  if (!res.ok) throw new Error(await readError(res));
  return (await res.json()) as PermissionDto;
}

export async function updatePermission(
  code: string,
  body: { description: string; module: string },
): Promise<void> {
  const res = await fetch(`/api/admin/iam/permissions/${encodeURIComponent(code)}`, {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
  if (!res.ok) throw new Error(await readError(res));
}

export async function deletePermission(code: string): Promise<void> {
  const res = await fetch(`/api/admin/iam/permissions/${encodeURIComponent(code)}`, {
    method: "DELETE",
  });
  if (!res.ok) throw new Error(await readError(res));
}

// ── Roles ────────────────────────────────────────────────────────────────

export async function listRoles(signal?: AbortSignal): Promise<RoleDto[]> {
  const res = await fetch("/api/admin/iam/roles", { signal, cache: "no-store" });
  if (!res.ok) throw new Error(`Failed to load roles: ${res.status}`);
  return (await res.json()) as RoleDto[];
}

export async function createRole(body: { name: string; description: string }): Promise<RoleDto> {
  const res = await fetch("/api/admin/iam/roles", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
  if (!res.ok) throw new Error(await readError(res));
  return (await res.json()) as RoleDto;
}

export async function deleteRole(name: string): Promise<void> {
  const res = await fetch(`/api/admin/iam/roles/${encodeURIComponent(name)}`, {
    method: "DELETE",
  });
  if (!res.ok) throw new Error(await readError(res));
}

export async function getRolePermissions(
  name: string,
  signal?: AbortSignal,
): Promise<string[]> {
  const res = await fetch(
    `/api/admin/iam/roles/${encodeURIComponent(name)}/permissions`,
    { signal, cache: "no-store" },
  );
  if (!res.ok) throw new Error(`Failed to load role permissions: ${res.status}`);
  return (await res.json()) as string[];
}

export async function grantPermission(role: string, code: string): Promise<void> {
  const res = await fetch(
    `/api/admin/iam/roles/${encodeURIComponent(role)}/permissions/${encodeURIComponent(code)}`,
    { method: "POST" },
  );
  if (!res.ok) throw new Error(await readError(res));
}

export async function revokePermission(role: string, code: string): Promise<void> {
  const res = await fetch(
    `/api/admin/iam/roles/${encodeURIComponent(role)}/permissions/${encodeURIComponent(code)}`,
    { method: "DELETE" },
  );
  if (!res.ok) throw new Error(await readError(res));
}

// ── Audit log ────────────────────────────────────────────────────────────

export async function queryAuditLog(params: {
  actor?: string;
  role?: string;
  action?: string;
  page?: number;
  pageSize?: number;
  signal?: AbortSignal;
}): Promise<AuditLogPage> {
  const search = new URLSearchParams();
  if (params.actor) search.set("actor", params.actor);
  if (params.role) search.set("role", params.role);
  if (params.action) search.set("action", params.action);
  if (params.page) search.set("page", String(params.page));
  if (params.pageSize) search.set("pageSize", String(params.pageSize));

  const qs = search.toString();
  const url = `/api/admin/iam/audit-log${qs ? `?${qs}` : ""}`;
  const res = await fetch(url, { signal: params.signal, cache: "no-store" });
  if (!res.ok) throw new Error(`Failed to load audit log: ${res.status}`);
  return (await res.json()) as AuditLogPage;
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

async function readError(res: Response): Promise<string> {
  try {
    const body = (await res.json()) as { error?: string; message?: string };
    return body.error ?? body.message ?? `Request failed: ${res.status}`;
  } catch {
    return `Request failed: ${res.status}`;
  }
}
