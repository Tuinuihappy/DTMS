"use client";

import { AlertCircle, ArrowLeft, RefreshCw, Trash2 } from "lucide-react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useCallback, useEffect, useState } from "react";
import {
  deleteRole,
  getRolePermissions,
  grantPermission,
  listPermissions,
  listRoles,
  revokePermission,
  type PermissionDto,
  type RoleDto,
} from "@/lib/api/iam";
import { PermissionsChecklist } from "@/components/admin/permissions-checklist";
import { cn } from "@/lib/utils";

// Phase S.7 — role detail page. Mirrors the system detail UX: header
// strip with metadata + actions, then a single PermissionsChecklist
// card. No matrix here — that lived on the old /admin/roles page
// before this drill-down split.

export function IamRoleDetailExperience({ roleName }: { roleName: string }) {
  const router = useRouter();
  const [role, setRole] = useState<RoleDto | null>(null);
  const [granted, setGranted] = useState<string[]>([]);
  const [catalog, setCatalog] = useState<PermissionDto[] | null>(null);
  const [catalogError, setCatalogError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  // Codes currently in flight (grant or revoke). Drives per-row spinner.
  const [togglingCodes, setTogglingCodes] = useState<Set<string>>(new Set());

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      // No GET /roles/{name} endpoint — list + filter is fine since the
      // role list is small (<10 typically) and already cached server-side.
      const [roles, perms] = await Promise.all([
        listRoles(),
        getRolePermissions(roleName),
      ]);
      const found = roles.find((r) => r.name === roleName) ?? null;
      setRole(found);
      setGranted(perms);
      if (!found) setError(`Role "${roleName}" not found.`);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setLoading(false);
    }
  }, [roleName]);

  useEffect(() => {
    void load();
  }, [load]);

  // Catalog loads once when the page mounts. The checklist still
  // renders without it (shows empty groups + the granted "Other"
  // group) so a catalog outage isn't fatal.
  useEffect(() => {
    const abort = new AbortController();
    listPermissions(abort.signal)
      .then((rows) => setCatalog(rows))
      .catch((e) => {
        if (!abort.signal.aborted) setCatalogError((e as Error).message);
      });
    return () => abort.abort();
  }, []);

  const onTogglePermission = async (code: string, currentlyGranted: boolean) => {
    setTogglingCodes((prev) => new Set(prev).add(code));
    setError(null);
    try {
      if (currentlyGranted) {
        await revokePermission(roleName, code);
      } else {
        await grantPermission(roleName, code);
      }
      // Refetch just this role's permissions — keeps the catalog stable.
      setGranted(await getRolePermissions(roleName));
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setTogglingCodes((prev) => {
        const next = new Set(prev);
        next.delete(code);
        return next;
      });
    }
  };

  const onDelete = async () => {
    if (!role) return;
    if (
      !confirm(
        `Delete role "${role.name}"?\n\nAll permission mappings for this role will be removed. Users currently carrying this role in their JWT will silently lose every DTMS permission within the cache TTL (≤ 5 minutes).`,
      )
    )
      return;
    setBusy(true);
    try {
      await deleteRole(role.name);
      router.push("/admin/roles");
    } catch (e) {
      setError((e as Error).message);
      setBusy(false);
    }
  };

  if (loading && !role) {
    return <div className="px-2 py-6 text-sm text-[var(--color-ink-400)]">Loading…</div>;
  }
  if (!role) {
    return (
      <div className="px-2 py-6">
        <p className="text-sm text-rose-600">{error ?? "Role not found"}</p>
        <Link href="/admin/roles" className="mt-2 inline-block text-xs text-[var(--color-brand-600)] hover:underline">
          ← Back to roles
        </Link>
      </div>
    );
  }

  return (
    <div className="space-y-6">
      <header className="space-y-2">
        <Link
          href="/admin/roles"
          className="inline-flex items-center gap-1 text-[11.5px] text-[var(--color-ink-500)] hover:text-[var(--color-ink-700)]"
        >
          <ArrowLeft className="h-3 w-3" strokeWidth={2.2} />
          All roles
        </Link>
        <div className="flex items-end justify-between gap-3">
          <div>
            <div className="flex items-baseline gap-3">
              <h1 className="text-[1.7rem] font-semibold text-[var(--color-ink-900)]">{role.name}</h1>
              {role.isSystem && (
                <span className="rounded-full bg-[var(--color-ink-100)] px-2 py-0.5 text-[10px] uppercase tracking-wide text-[var(--color-ink-700)]">
                  system role
                </span>
              )}
            </div>
            {role.description && (
              <p className="mt-1 text-[12px] text-[var(--color-ink-500)]">{role.description}</p>
            )}
            <p className="mt-1 text-[11px] text-[var(--color-ink-400)]">
              {granted.length} permission{granted.length === 1 ? "" : "s"} granted · changes propagate within 5 min cache TTL
            </p>
          </div>
          <div className="flex items-center gap-2">
            <button
              type="button"
              onClick={load}
              disabled={busy || loading}
              className="inline-flex items-center gap-1 rounded-full border border-[var(--color-ink-100)] bg-white/70 px-3 py-1.5 text-[11.5px] font-medium text-[var(--color-ink-600)] hover:bg-white disabled:opacity-50 dark:bg-white/[0.05]"
            >
              <RefreshCw className={cn("h-3 w-3", (busy || loading) && "animate-spin")} strokeWidth={2.4} />
              Refresh
            </button>
            {!role.isSystem && (
              <button
                type="button"
                disabled={busy}
                onClick={onDelete}
                className="inline-flex items-center gap-1 rounded-full border border-rose-200 bg-white px-3 py-1.5 text-[11.5px] font-medium text-rose-600 hover:bg-rose-50 disabled:opacity-50"
              >
                <Trash2 className="h-3 w-3" strokeWidth={2.2} />
                Delete role
              </button>
            )}
          </div>
        </div>
      </header>

      {error && (
        <div className="flex items-start gap-2 rounded-md border border-rose-300 bg-rose-50 px-3 py-2 text-[12px] text-rose-700">
          <AlertCircle className="mt-0.5 h-3.5 w-3.5 flex-shrink-0" strokeWidth={2.2} />
          <span>{error}</span>
        </div>
      )}

      <PermissionsChecklist
        granted={granted}
        catalog={catalog}
        catalogError={catalogError}
        toggling={togglingCodes}
        onToggle={onTogglePermission}
        hint="Tick a row to grant; untick to revoke. Changes propagate to authenticated users within 5 minutes (permission cache TTL)."
      />
    </div>
  );
}
