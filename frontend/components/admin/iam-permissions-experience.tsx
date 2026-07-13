"use client";

import { AlertCircle, RefreshCw } from "lucide-react";
import { useCallback, useEffect, useMemo, useState } from "react";
import { listPermissions, type PermissionDto } from "@/lib/api/iam";
import { GlassCard } from "@/components/primitives/glass-card";
import { cn } from "@/lib/utils";

// Permission catalog viewer (read-only). The catalog is defined in code
// (`Permissions.All`) and served straight from there — adding or renaming a
// permission is a code change + deploy, not a runtime edit. This page is a
// reference view of what the API enforces; grants happen on the role and
// system detail pages.

export function IamPermissionsExperience() {
  const [items, setItems] = useState<PermissionDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

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

  // Group by module for the list view; modules sorted alphabetically.
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
            All <code className="font-mono">dtms:*</code> permissions the API enforces —
            defined in code and read-only here. Add or rename one via a code change + deploy.
          </p>
        </div>
        <button
          type="button"
          onClick={load}
          className="inline-flex items-center gap-1.5 rounded-full border border-[var(--color-ink-100)] bg-white/70 px-3 py-1.5 text-[11.5px] font-medium text-[var(--color-ink-600)] hover:bg-white dark:bg-white/[0.05] dark:hover:bg-white/[0.12]"
        >
          <RefreshCw className={cn("h-3 w-3", loading && "animate-spin")} strokeWidth={2.4} />
          Refresh
        </button>
      </header>

      {error ? (
        <GlassCard className="flex items-center gap-2 border-rose-200 bg-rose-50/60 px-4 py-3 text-[13px] text-rose-700">
          <AlertCircle className="h-4 w-4" />
          {error}
        </GlassCard>
      ) : null}

      {grouped.length === 0 && !loading ? (
        <GlassCard className="px-6 py-10 text-center text-[13px] text-[var(--color-ink-500)]">
          No permissions found.
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
                  </tr>
                ))}
              </tbody>
            </table>
          </GlassCard>
        </section>
      ))}
    </div>
  );
}
