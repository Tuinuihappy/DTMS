"use client";

import { AlertCircle, Pencil, Plus, RefreshCw, Trash2, X } from "lucide-react";
import { useCallback, useEffect, useMemo, useState } from "react";
import {
  createPermission,
  deletePermission,
  listPermissions,
  updatePermission,
  type PermissionDto,
} from "@/lib/api/iam";
import { GlassCard } from "@/components/primitives/glass-card";
import { cn } from "@/lib/utils";

// Permission catalog management page (Phase B.2). The catalog is mostly
// seeded from migrations — admin CRUD here is the escape hatch for
// adding/renaming/removing perms without a deploy. Backend rejects codes
// not prefixed with "dtms:" so the form mirrors that validation.

export function IamPermissionsExperience() {
  const [items, setItems] = useState<PermissionDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [editing, setEditing] = useState<PermissionDto | null>(null);
  const [creating, setCreating] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      setItems(await listPermissions());
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  // Group by module for the list view. Sort modules alphabetically; perms
  // stay in the order the API returns (code asc within module).
  const grouped = useMemo(() => {
    const byModule = new Map<string, PermissionDto[]>();
    for (const p of items) {
      const list = byModule.get(p.module) ?? [];
      list.push(p);
      byModule.set(p.module, list);
    }
    return [...byModule.entries()].sort((a, b) => a[0].localeCompare(b[0]));
  }, [items]);

  return (
    <div className="space-y-6">
      <header className="flex flex-col gap-2 sm:flex-row sm:items-end sm:justify-between">
        <div>
          <h1 className="text-[1.7rem] font-semibold text-[var(--color-ink-900)]">
            Permission catalog
          </h1>
          <p className="mt-1 text-[13px] text-[var(--color-ink-500)]">
            All <code className="font-mono">dtms:*</code> permissions the API enforces.
            Cache TTL is 5 minutes — changes propagate to live sessions within that window.
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
            New permission
          </button>
        </div>
      </header>

      {error ? (
        <GlassCard className="flex items-center gap-2 border-rose-200 bg-rose-50/60 px-4 py-3 text-[13px] text-rose-700">
          <AlertCircle className="h-4 w-4" />
          {error}
        </GlassCard>
      ) : null}

      {grouped.length === 0 && !loading ? (
        <GlassCard className="px-6 py-10 text-center text-[13px] text-[var(--color-ink-500)]">
          No permissions yet.
        </GlassCard>
      ) : null}

      {grouped.map(([module, perms]) => (
        <section key={module} className="space-y-2">
          <h2 className="text-[14px] font-semibold uppercase tracking-wide text-[var(--color-ink-700)]">
            {module} <span className="text-[var(--color-ink-400)]">· {perms.length}</span>
          </h2>
          <GlassCard className="overflow-hidden p-0">
            <table className="w-full text-[13px]">
              <thead>
                <tr className="border-b border-[var(--color-ink-100)] text-left text-[11.5px] uppercase tracking-wide text-[var(--color-ink-500)]">
                  <th className="px-4 py-2 font-medium">Code</th>
                  <th className="px-4 py-2 font-medium">Description</th>
                  <th className="px-4 py-2 text-right font-medium">Actions</th>
                </tr>
              </thead>
              <tbody>
                {perms.map((p) => (
                  <tr
                    key={p.code}
                    className="border-b border-[var(--color-ink-100)]/60 last:border-b-0 hover:bg-white/40"
                  >
                    <td className="px-4 py-2.5 font-mono text-[12px] text-[var(--color-ink-900)]">
                      {p.code}
                    </td>
                    <td className="px-4 py-2.5 text-[var(--color-ink-700)]">
                      {p.description}
                    </td>
                    <td className="px-4 py-2.5 text-right">
                      <div className="inline-flex items-center gap-1">
                        <button
                          type="button"
                          onClick={() => setEditing(p)}
                          className="inline-flex h-7 w-7 items-center justify-center rounded-full text-[var(--color-ink-500)] hover:bg-[var(--color-ink-50)] hover:text-[var(--color-ink-900)]"
                          title="Edit"
                        >
                          <Pencil className="h-3.5 w-3.5" />
                        </button>
                        <button
                          type="button"
                          onClick={async () => {
                            if (!confirm(`Delete permission ${p.code}?\n\nThis also revokes any role mappings using it.`)) return;
                            try {
                              await deletePermission(p.code);
                              await load();
                            } catch (e) {
                              alert((e as Error).message);
                            }
                          }}
                          className="inline-flex h-7 w-7 items-center justify-center rounded-full text-[var(--color-ink-500)] hover:bg-rose-50 hover:text-rose-600"
                          title="Delete"
                        >
                          <Trash2 className="h-3.5 w-3.5" />
                        </button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </GlassCard>
        </section>
      ))}

      {(creating || editing) ? (
        <PermissionDialog
          editing={editing}
          onClose={() => {
            setCreating(false);
            setEditing(null);
          }}
          onSaved={async () => {
            setCreating(false);
            setEditing(null);
            await load();
          }}
        />
      ) : null}
    </div>
  );
}

function PermissionDialog({
  editing,
  onClose,
  onSaved,
}: {
  editing: PermissionDto | null;
  onClose: () => void;
  onSaved: () => void | Promise<void>;
}) {
  const isEdit = editing !== null;
  const [code, setCode] = useState(editing?.code ?? "dtms:");
  const [description, setDescription] = useState(editing?.description ?? "");
  const [module, setModule] = useState(editing?.module ?? "");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit() {
    setSubmitting(true);
    setError(null);
    try {
      if (isEdit) {
        await updatePermission(editing!.code, { description, module });
      } else {
        await createPermission({ code, description, module });
      }
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
          <h2 className="text-[18px] font-semibold text-[var(--color-ink-900)]">
            {isEdit ? "Edit permission" : "New permission"}
          </h2>
          <button
            type="button"
            onClick={onClose}
            className="rounded-full p-1 text-[var(--color-ink-500)] hover:bg-[var(--color-ink-50)]"
          >
            <X className="h-4 w-4" />
          </button>
        </div>

        <div className="mt-4 space-y-3">
          <div>
            <label className="text-[11.5px] font-medium uppercase tracking-wide text-[var(--color-ink-500)]">
              Code
            </label>
            <input
              type="text"
              value={code}
              onChange={(e) => setCode(e.target.value)}
              disabled={isEdit}
              placeholder="dtms:module:resource:action"
              className="mt-1 w-full rounded-lg border border-[var(--color-ink-100)] bg-white px-3 py-2 font-mono text-[12.5px] focus:outline-none focus:ring-2 focus:ring-[var(--color-ink-900)]/20 disabled:bg-[var(--color-ink-50)] disabled:text-[var(--color-ink-500)]"
            />
            {!isEdit ? (
              <p className="mt-1 text-[11px] text-[var(--color-ink-500)]">
                Must start with <code className="font-mono">dtms:</code>. Cannot be changed after creation.
              </p>
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
              placeholder="Read facility maps"
              className="mt-1 w-full rounded-lg border border-[var(--color-ink-100)] bg-white px-3 py-2 text-[13px] focus:outline-none focus:ring-2 focus:ring-[var(--color-ink-900)]/20"
            />
          </div>

          <div>
            <label className="text-[11.5px] font-medium uppercase tracking-wide text-[var(--color-ink-500)]">
              Module
            </label>
            <input
              type="text"
              value={module}
              onChange={(e) => setModule(e.target.value)}
              placeholder="Facility"
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
            disabled={submitting || !code.trim() || (!isEdit && !code.startsWith("dtms:"))}
            className="rounded-full bg-[var(--color-ink-900)] px-4 py-1.5 text-[12px] font-semibold text-white hover:bg-[var(--color-ink-700)] disabled:cursor-not-allowed disabled:opacity-50"
          >
            {submitting ? "Saving…" : isEdit ? "Save" : "Create"}
          </button>
        </div>
      </div>
    </div>
  );
}
