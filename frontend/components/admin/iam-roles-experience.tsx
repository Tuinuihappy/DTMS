"use client";

import { AlertCircle, ChevronRight, Loader2, Plus, RefreshCw, Trash2, X } from "lucide-react";
import Link from "next/link";
import { useCallback, useEffect, useMemo, useState } from "react";
import {
  createRole,
  deleteRole,
  getRolePermissions,
  listRoles,
  revokePermission,
  type RoleDto,
} from "@/lib/api/iam";
import { cn } from "@/lib/utils";

// Phase S.7 — roles list (drill-down). Replaces the matrix UI with a
// per-role card; click a card to land on /admin/roles/{name} where the
// shared PermissionsChecklist handles grant/revoke (same UX as the
// system detail page). Wildcards stay visible on each card as a quick
// summary, and can be revoked inline without leaving the list.

export function IamRolesExperience() {
  const [roles, setRoles] = useState<RoleDto[]>([]);
  const [rolePerms, setRolePerms] = useState<Map<string, string[]>>(new Map());
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [creating, setCreating] = useState(false);
  // Per-row busy tracking for inline wildcard revoke + role delete.
  const [busy, setBusy] = useState<Set<string>>(new Set());

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const roleList = await listRoles();
      setRoles(roleList);
      // allSettled — one role with a bad/legacy name shouldn't blank
      // the rest of the cards. The failing one just shows zero perms.
      const results = await Promise.allSettled(
        roleList.map(async (r) => [r.name, await getRolePermissions(r.name)] as const),
      );
      const entries = results
        .filter((r): r is PromiseFulfilledResult<readonly [string, string[]]> => r.status === "fulfilled")
        .map((r) => r.value);
      setRolePerms(new Map(entries));
      const firstFail = results.find((r) => r.status === "rejected") as PromiseRejectedResult | undefined;
      if (firstFail) {
        setError(`Couldn't load permissions for one or more roles: ${(firstFail.reason as Error).message}`);
      }
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const wildcardsByRole = useMemo(() => {
    const map = new Map<string, string[]>();
    for (const [role, codes] of rolePerms.entries()) {
      map.set(role, codes.filter((c) => c.endsWith(":*")));
    }
    return map;
  }, [rolePerms]);

  const markBusy = (key: string) => setBusy((prev) => new Set(prev).add(key));
  const clearBusy = (key: string) =>
    setBusy((prev) => {
      const next = new Set(prev);
      next.delete(key);
      return next;
    });

  const revokeWildcard = async (roleName: string, code: string) => {
    const key = `${roleName}::${code}`;
    markBusy(key);
    try {
      await revokePermission(roleName, code);
      const updated = await getRolePermissions(roleName);
      setRolePerms((prev) => {
        const next = new Map(prev);
        next.set(roleName, updated);
        return next;
      });
    } catch (e) {
      alert((e as Error).message);
    } finally {
      clearBusy(key);
    }
  };

  const handleDelete = async (roleName: string) => {
    if (
      !confirm(
        `Delete role "${roleName}"?\n\nAll permission mappings for this role will be removed. Users currently carrying this role in their JWT will silently lose every DTMS permission within the cache TTL (≤ 5 minutes).`,
      )
    )
      return;
    const key = `delete::${roleName}`;
    markBusy(key);
    try {
      await deleteRole(roleName);
      await load();
    } catch (e) {
      alert((e as Error).message);
    } finally {
      clearBusy(key);
    }
  };

  return (
    <div className="space-y-6">
      <header className="flex flex-col gap-2 sm:flex-row sm:items-end sm:justify-between">
        <div>
          <h1 className="text-[1.7rem] font-semibold text-[var(--color-ink-900)]">Roles &amp; permissions</h1>
          <p className="mt-1 text-[13px] text-[var(--color-ink-500)]">
            External Auth assigns roles to users in the JWT; this page maps each role to the
            permissions it holds at DTMS. Click a role to manage its permissions. Changes take
            effect within 5 minutes (cache TTL).
          </p>
        </div>
        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={load}
            className="inline-flex items-center gap-1.5 rounded-full border border-[var(--color-ink-100)] bg-white/70 px-3 py-1.5 text-[11.5px] font-medium text-[var(--color-ink-600)] hover:bg-white dark:bg-white/[0.05] dark:hover:bg-white/[0.12]"
          >
            <RefreshCw className={cn("h-3 w-3", loading && "animate-spin")} strokeWidth={2.4} />
            Refresh
          </button>
          <button
            type="button"
            onClick={() => setCreating(true)}
            className="inline-flex items-center gap-1.5 rounded-full bg-[var(--color-ink-900)] px-3 py-1.5 text-[11.5px] font-semibold text-white hover:bg-[var(--color-ink-700)]"
          >
            <Plus className="h-3 w-3" strokeWidth={2.4} />
            New role
          </button>
        </div>
      </header>

      {error && (
        <div className="flex items-start gap-2 rounded-md border border-rose-300 bg-rose-50 px-3 py-2 text-[12px] text-rose-700 dark:border-rose-500/30 dark:bg-rose-950/40 dark:text-rose-300">
          <AlertCircle className="mt-0.5 h-3.5 w-3.5 flex-shrink-0" strokeWidth={2.2} />
          <span>{error}</span>
          <button
            type="button"
            onClick={() => setError(null)}
            className="ml-auto text-rose-600 hover:text-rose-800 dark:text-rose-300 dark:hover:text-rose-100"
            aria-label="Dismiss error"
          >
            <X className="h-3 w-3" strokeWidth={2.4} />
          </button>
        </div>
      )}

      <div className="overflow-hidden rounded-xl border border-[var(--color-ink-100)] bg-white/70 dark:border-white/[0.06] dark:bg-white/[0.03]">
        <table className="w-full text-left text-[12.5px]">
          <thead className="bg-[var(--color-ink-50)] text-[10.5px] uppercase tracking-[0.08em] text-[var(--color-ink-500)] dark:bg-white/[0.04]">
            <tr>
              <th className="px-3 py-2 font-semibold">Name</th>
              <th className="px-3 py-2 font-semibold">Description</th>
              <th className="px-3 py-2 font-semibold">Type</th>
              <th className="px-3 py-2 font-semibold">Granted</th>
              <th className="px-3 py-2 font-semibold">Wildcards</th>
              <th className="px-3 py-2 font-semibold">Actions</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-[var(--color-ink-100)] dark:divide-white/[0.04]">
            {loading && roles.length === 0 ? (
              <tr>
                <td colSpan={6} className="px-3 py-6 text-center text-[var(--color-ink-400)]">
                  Loading…
                </td>
              </tr>
            ) : roles.length === 0 ? (
              <tr>
                <td colSpan={6} className="px-3 py-6 text-center text-[var(--color-ink-400)]">
                  No roles yet. Click <em>New role</em> to create one.
                </td>
              </tr>
            ) : (
              roles.map((r) => {
                const wildcards = wildcardsByRole.get(r.name) ?? [];
                const grantedCount = rolePerms.get(r.name)?.length ?? 0;
                const deleteKey = `delete::${r.name}`;
                const isDeleting = busy.has(deleteKey);
                return (
                  <tr key={r.name} className="hover:bg-[var(--color-ink-50)]/50 dark:hover:bg-white/[0.03]">
                    <td className="px-3 py-2 font-mono text-[12px]">
                      <Link
                        href={`/admin/roles/${encodeURIComponent(r.name)}`}
                        className="text-[var(--color-brand-600)] hover:underline"
                      >
                        {r.name}
                      </Link>
                    </td>
                    <td className="px-3 py-2 text-[var(--color-ink-700)]">
                      {r.description || <span className="text-[var(--color-ink-400)]">—</span>}
                    </td>
                    <td className="px-3 py-2">
                      {r.isSystem ? (
                        <span className="inline-flex items-center rounded-full bg-[var(--color-ink-100)] px-2 py-0.5 text-[10.5px] font-medium uppercase tracking-wide text-[var(--color-ink-700)] dark:bg-white/[0.06]">
                          system
                        </span>
                      ) : (
                        <span className="inline-flex items-center rounded-full bg-emerald-100 px-2 py-0.5 text-[10.5px] font-medium uppercase tracking-wide text-emerald-800 dark:bg-emerald-900/40 dark:text-emerald-300">
                          custom
                        </span>
                      )}
                    </td>
                    <td className="px-3 py-2 text-[var(--color-ink-700)]">
                      <span className="font-mono">{grantedCount}</span>
                    </td>
                    <td className="px-3 py-2">
                      {wildcards.length === 0 ? (
                        <span className="text-[var(--color-ink-400)]">—</span>
                      ) : (
                        <div className="flex flex-wrap gap-1">
                          {wildcards.map((w) => {
                            const key = `${r.name}::${w}`;
                            const isBusy = busy.has(key);
                            return (
                              <button
                                key={w}
                                type="button"
                                disabled={isBusy}
                                onClick={() => void revokeWildcard(r.name, w)}
                                className="inline-flex items-center gap-1 rounded-full bg-amber-100 px-2 py-0.5 font-mono text-[11px] text-amber-900 hover:bg-amber-200 disabled:opacity-50 dark:bg-amber-900/40 dark:text-amber-300"
                                title="Wildcard grant — click to revoke"
                              >
                                {w}
                                {isBusy ? <Loader2 className="h-2.5 w-2.5 animate-spin" /> : <X className="h-2.5 w-2.5" />}
                              </button>
                            );
                          })}
                        </div>
                      )}
                    </td>
                    <td className="px-3 py-2">
                      <div className="flex items-center gap-1">
                        <Link
                          href={`/admin/roles/${encodeURIComponent(r.name)}`}
                          className="inline-flex items-center gap-1 rounded border border-[var(--color-ink-100)] bg-white px-2 py-1 text-[11px] text-[var(--color-ink-600)] hover:bg-[var(--color-ink-50)] dark:bg-white/[0.05]"
                          title="Manage permissions"
                        >
                          <ChevronRight className="h-3 w-3" strokeWidth={2.2} />
                          Manage
                        </Link>
                        {!r.isSystem && (
                          <button
                            type="button"
                            disabled={isDeleting}
                            onClick={() => void handleDelete(r.name)}
                            className="inline-flex items-center gap-1 rounded border border-[var(--color-ink-100)] bg-white px-2 py-1 text-[11px] text-rose-600 hover:bg-rose-50 disabled:opacity-50 dark:bg-white/[0.05]"
                            title="Delete role"
                          >
                            {isDeleting ? (
                              <Loader2 className="h-3 w-3 animate-spin" />
                            ) : (
                              <Trash2 className="h-3 w-3" strokeWidth={2.2} />
                            )}
                            Delete
                          </button>
                        )}
                      </div>
                    </td>
                  </tr>
                );
              })
            )}
          </tbody>
        </table>
      </div>

      {creating ? (
        <CreateRoleDialog
          onClose={() => setCreating(false)}
          onSaved={async () => {
            setCreating(false);
            await load();
          }}
        />
      ) : null}
    </div>
  );
}

function CreateRoleDialog({
  onClose,
  onSaved,
}: {
  onClose: () => void;
  onSaved: () => void | Promise<void>;
}) {
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const invalidChars = /[\/?#%\\\s]/;
  const nameError = name && invalidChars.test(name) ? "Cannot contain /, ?, #, %, \\, or spaces." : null;

  async function submit() {
    if (nameError) return;
    setSubmitting(true);
    setError(null);
    try {
      await createRole({ name, description });
      await onSaved();
    } catch (e) {
      setError((e as Error).message);
      setSubmitting(false);
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/30 backdrop-blur-sm">
      <div className="w-full max-w-md rounded-2xl border border-[var(--color-ink-100)] bg-white p-6 shadow-2xl dark:bg-[var(--color-ink-50)]">
        <div className="flex items-start justify-between">
          <h2 className="text-[18px] font-semibold text-[var(--color-ink-900)]">New role</h2>
          <button
            type="button"
            onClick={onClose}
            className="rounded-full p-1 text-[var(--color-ink-500)] hover:bg-[var(--color-ink-50)]"
          >
            <X className="h-4 w-4" />
          </button>
        </div>

        <p className="mt-3 rounded-lg border border-amber-200 bg-amber-50 px-3 py-2 text-[11.5px] text-amber-900">
          External Auth must also issue JWTs with this role name — otherwise no user will ever
          carry it.
        </p>

        <div className="mt-4 space-y-3">
          <div>
            <label className="text-[11.5px] font-medium uppercase tracking-wide text-[var(--color-ink-500)]">Name</label>
            <input
              type="text"
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="Dispatcher"
              className="mt-1 w-full rounded-lg border border-[var(--color-ink-100)] bg-white px-3 py-2 text-[13px] focus:outline-none focus:ring-2 focus:ring-[var(--color-ink-900)]/20"
            />
            <p className="mt-1 text-[11px] text-[var(--color-ink-500)]">
              Case-sensitive — must match the value External Auth puts in the JWT role claim.
            </p>
            {nameError ? <p className="mt-1 text-[11px] text-rose-600">{nameError}</p> : null}
          </div>

          <div>
            <label className="text-[11.5px] font-medium uppercase tracking-wide text-[var(--color-ink-500)]">
              Description
            </label>
            <input
              type="text"
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              placeholder="Manages trip dispatch + reissue"
              className="mt-1 w-full rounded-lg border border-[var(--color-ink-100)] bg-white px-3 py-2 text-[13px] focus:outline-none focus:ring-2 focus:ring-[var(--color-ink-900)]/20"
            />
          </div>

          {error ? <div className="rounded-lg bg-rose-50 px-3 py-2 text-[12px] text-rose-700">{error}</div> : null}
        </div>

        <div className="mt-6 flex items-center justify-end gap-2">
          <button
            type="button"
            onClick={onClose}
            className="rounded-full border border-[var(--color-ink-100)] bg-white px-4 py-1.5 text-[12px] font-medium text-[var(--color-ink-700)] hover:bg-[var(--color-ink-50)]"
          >
            Cancel
          </button>
          <button
            type="button"
            onClick={submit}
            disabled={submitting || !name.trim() || !!nameError}
            className="rounded-full bg-[var(--color-ink-900)] px-4 py-1.5 text-[12px] font-semibold text-white hover:bg-[var(--color-ink-700)] disabled:cursor-not-allowed disabled:opacity-50"
          >
            {submitting ? "Saving…" : "Create"}
          </button>
        </div>
      </div>
    </div>
  );
}
