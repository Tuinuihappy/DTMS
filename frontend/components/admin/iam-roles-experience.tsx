"use client";

import { AlertCircle, Check, Loader2, Plus, RefreshCw, Trash2, X } from "lucide-react";
import { useCallback, useEffect, useMemo, useState } from "react";
import {
  createRole,
  deleteRole,
  getRolePermissions,
  grantPermission,
  listPermissions,
  listRoles,
  matches,
  revokePermission,
  type PermissionDto,
  type RoleDto,
} from "@/lib/api/iam";
import { GlassCard } from "@/components/primitives/glass-card";
import { cn } from "@/lib/utils";

// Roles + permission matrix (Phase B.2 main page). Rows are catalog
// permissions, columns are roles, cells are check/uncheck toggles that
// call grant/revoke immediately (no batch save). Wildcards held by a
// role (e.g. dtms:* on Admin) appear as gray "covered" cells — they
// can't be unchecked at the row level because the wildcard itself is
// the grant; admin must revoke the wildcard from the role's perms list
// (chip below the matrix) to remove coverage.

export function IamRolesExperience() {
  const [roles, setRoles] = useState<RoleDto[]>([]);
  const [permissions, setPermissions] = useState<PermissionDto[]>([]);
  const [rolePerms, setRolePerms] = useState<Map<string, string[]>>(new Map());
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [creating, setCreating] = useState(false);
  // Track in-flight cell toggles so the UI can show a spinner on the
  // specific (role, perm) instead of disabling the whole grid.
  const [busy, setBusy] = useState<Set<string>>(new Set());

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const [roleList, permList] = await Promise.all([listRoles(), listPermissions()]);
      setRoles(roleList);
      setPermissions(permList);
      // allSettled — one role with a bad/legacy name (e.g. containing '/'
      // before the validation rule landed) shouldn't blank the rest of
      // the matrix. The failing column just shows empty cells.
      const results = await Promise.allSettled(
        roleList.map(async (r) => [r.name, await getRolePermissions(r.name)] as const),
      );
      const entries = results
        .filter((r): r is PromiseFulfilledResult<readonly [string, string[]]> => r.status === "fulfilled")
        .map((r) => r.value);
      setRolePerms(new Map(entries));
      // Surface the first failure (if any) as a warning banner so the
      // admin notices a bad row without blocking the whole UI.
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

  const grouped = useMemo(() => {
    const byModule = new Map<string, PermissionDto[]>();
    for (const p of permissions) {
      const list = byModule.get(p.module) ?? [];
      list.push(p);
      byModule.set(p.module, list);
    }
    return [...byModule.entries()].sort((a, b) => a[0].localeCompare(b[0]));
  }, [permissions]);

  // Wildcards each role holds — surfaced as chips so admin can revoke
  // them without scrolling the matrix.
  const wildcardsByRole = useMemo(() => {
    const map = new Map<string, string[]>();
    for (const [role, codes] of rolePerms.entries()) {
      map.set(
        role,
        codes.filter((c) => c.endsWith(":*")),
      );
    }
    return map;
  }, [rolePerms]);

  async function toggle(role: string, code: string, currentlyExplicit: boolean) {
    const key = `${role}::${code}`;
    setBusy((prev) => new Set(prev).add(key));
    try {
      if (currentlyExplicit) {
        await revokePermission(role, code);
      } else {
        await grantPermission(role, code);
      }
      // Refetch just this role's perms — keeps the rest of the grid stable.
      const updated = await getRolePermissions(role);
      setRolePerms((prev) => {
        const next = new Map(prev);
        next.set(role, updated);
        return next;
      });
    } catch (e) {
      alert((e as Error).message);
    } finally {
      setBusy((prev) => {
        const next = new Set(prev);
        next.delete(key);
        return next;
      });
    }
  }

  return (
    <div className="space-y-6">
      <header className="flex flex-col gap-2 sm:flex-row sm:items-end sm:justify-between">
        <div>
          <h1 className="text-[1.7rem] font-semibold text-[var(--color-ink-900)]">
            Roles &amp; permissions
          </h1>
          <p className="mt-1 text-[13px] text-[var(--color-ink-500)]">
            External Auth assigns roles to users in the JWT; this page maps each
            role to the permissions it holds at DTMS. Toggles take effect within
            5 minutes (cache TTL).
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

      {error ? (
        <GlassCard className="flex items-center gap-2 border-rose-200 bg-rose-50/60 px-4 py-3 text-[13px] text-rose-700">
          <AlertCircle className="h-4 w-4" />
          {error}
        </GlassCard>
      ) : null}

      {/* Role summary strip with wildcard chips + delete affordance */}
      <section className="space-y-2">
        <h2 className="text-[14px] font-semibold uppercase tracking-wide text-[var(--color-ink-700)]">
          Roles <span className="text-[var(--color-ink-400)]">· {roles.length}</span>
        </h2>
        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-3">
          {roles.map((r) => {
            const wildcards = wildcardsByRole.get(r.name) ?? [];
            return (
              <GlassCard key={r.name} className="flex flex-col gap-2 px-4 py-3">
                <div className="flex items-start justify-between gap-2">
                  <div className="min-w-0">
                    <div className="flex items-center gap-2">
                      <span className="font-semibold text-[var(--color-ink-900)]">{r.name}</span>
                      {r.isSystem ? (
                        <span className="rounded-full bg-[var(--color-ink-100)] px-2 py-0.5 text-[10px] uppercase tracking-wide text-[var(--color-ink-700)]">
                          system
                        </span>
                      ) : null}
                    </div>
                    {r.description ? (
                      <p className="mt-0.5 text-[12px] text-[var(--color-ink-500)]">{r.description}</p>
                    ) : null}
                  </div>
                  {!r.isSystem ? (
                    <button
                      type="button"
                      onClick={async () => {
                        if (!confirm(`Delete role "${r.name}"?\n\nAll permission mappings for this role will be removed.`)) return;
                        try {
                          await deleteRole(r.name);
                          await load();
                        } catch (e) {
                          alert((e as Error).message);
                        }
                      }}
                      className="inline-flex h-7 w-7 items-center justify-center rounded-full text-[var(--color-ink-500)] hover:bg-rose-50 hover:text-rose-600"
                      title="Delete role"
                    >
                      <Trash2 className="h-3.5 w-3.5" />
                    </button>
                  ) : null}
                </div>
                {wildcards.length > 0 ? (
                  <div className="flex flex-wrap gap-1">
                    {wildcards.map((w) => {
                      const key = `${r.name}::${w}`;
                      const isBusy = busy.has(key);
                      return (
                        <button
                          key={w}
                          type="button"
                          disabled={isBusy}
                          onClick={() => toggle(r.name, w, true)}
                          className="group inline-flex items-center gap-1 rounded-full bg-amber-100 px-2 py-0.5 font-mono text-[11px] text-amber-900 hover:bg-amber-200 disabled:opacity-50"
                          title="Wildcard grant — click to revoke"
                        >
                          {w}
                          {isBusy ? (
                            <Loader2 className="h-2.5 w-2.5 animate-spin" />
                          ) : (
                            <X className="h-2.5 w-2.5 opacity-0 group-hover:opacity-100" />
                          )}
                        </button>
                      );
                    })}
                  </div>
                ) : null}
              </GlassCard>
            );
          })}
        </div>
      </section>

      {/* Matrix */}
      {grouped.map(([module, perms]) => (
        <section key={module} className="space-y-2">
          <h2 className="text-[14px] font-semibold uppercase tracking-wide text-[var(--color-ink-700)]">
            {module} <span className="text-[var(--color-ink-400)]">· {perms.length}</span>
          </h2>
          <GlassCard className="overflow-x-auto p-0">
            <table className="min-w-full text-[13px]">
              <thead>
                <tr className="border-b border-[var(--color-ink-100)] text-left text-[11.5px] uppercase tracking-wide text-[var(--color-ink-500)]">
                  <th className="sticky left-0 z-10 bg-white/90 px-4 py-2 font-medium backdrop-blur dark:bg-[var(--color-ink-50)]/90">
                    Permission
                  </th>
                  {roles.map((r) => (
                    <th key={r.name} className="px-3 py-2 text-center font-medium">
                      {r.name}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {perms.map((p) => (
                  <tr
                    key={p.code}
                    className="border-b border-[var(--color-ink-100)]/60 last:border-b-0 hover:bg-white/40"
                  >
                    <td className="sticky left-0 z-10 bg-white/90 px-4 py-2 font-mono text-[12px] text-[var(--color-ink-900)] backdrop-blur dark:bg-[var(--color-ink-50)]/90">
                      <div>{p.code}</div>
                      <div className="font-sans text-[11px] text-[var(--color-ink-500)]">{p.description}</div>
                    </td>
                    {roles.map((r) => {
                      const held = rolePerms.get(r.name) ?? [];
                      const explicit = held.includes(p.code);
                      const wildcard = !explicit && held.some((c) => c.endsWith(":*") && matches(c, p.code));
                      const key = `${r.name}::${p.code}`;
                      const isBusy = busy.has(key);
                      return (
                        <td key={r.name} className="px-3 py-2 text-center">
                          <button
                            type="button"
                            onClick={() => !wildcard && toggle(r.name, p.code, explicit)}
                            disabled={isBusy || wildcard}
                            className={cn(
                              "inline-flex h-6 w-6 items-center justify-center rounded-md border transition-colors",
                              explicit
                                ? "border-emerald-300 bg-emerald-100 text-emerald-700 hover:bg-emerald-200"
                                : wildcard
                                  ? "cursor-default border-amber-200 bg-amber-50 text-amber-600"
                                  : "border-[var(--color-ink-100)] bg-white hover:border-emerald-300 hover:bg-emerald-50",
                              isBusy && "opacity-50",
                            )}
                            title={
                              wildcard
                                ? "Covered by a wildcard grant (revoke the wildcard chip on the role to remove)"
                                : explicit
                                  ? "Click to revoke"
                                  : "Click to grant"
                            }
                          >
                            {isBusy ? (
                              <Loader2 className="h-3 w-3 animate-spin" />
                            ) : explicit ? (
                              <Check className="h-3.5 w-3.5" strokeWidth={3} />
                            ) : wildcard ? (
                              <span className="text-[10px]">*</span>
                            ) : null}
                          </button>
                        </td>
                      );
                    })}
                  </tr>
                ))}
              </tbody>
            </table>
          </GlassCard>
        </section>
      ))}

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
  const nameError = name && invalidChars.test(name)
    ? "Cannot contain /, ?, #, %, \\, or spaces."
    : null;

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
          External Auth must also issue JWTs with this role name — otherwise no
          user will ever carry it.
        </p>

        <div className="mt-4 space-y-3">
          <div>
            <label className="text-[11.5px] font-medium uppercase tracking-wide text-[var(--color-ink-500)]">
              Name
            </label>
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
            {nameError ? (
              <p className="mt-1 text-[11px] text-rose-600">{nameError}</p>
            ) : null}
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

          {error ? (
            <div className="rounded-lg bg-rose-50 px-3 py-2 text-[12px] text-rose-700">{error}</div>
          ) : null}
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
