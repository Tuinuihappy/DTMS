"use client";

import { AlertCircle, RefreshCw, X } from "lucide-react";
import { useCallback, useEffect, useState } from "react";
import { queryAuditLog, type AuditLogEntryDto, type AuditLogPage } from "@/lib/api/iam";
import { GlassCard } from "@/components/primitives/glass-card";
import { DateTime } from "@/components/primitives/date-time";
import { cn } from "@/lib/utils";

const ACTION_LABELS: Record<string, { label: string; tone: "emerald" | "rose" | "ink" | "amber" }> = {
  grant: { label: "Grant", tone: "emerald" },
  revoke: { label: "Revoke", tone: "rose" },
  "permission-created": { label: "Permission created", tone: "ink" },
  "permission-updated": { label: "Permission updated", tone: "amber" },
  "permission-deleted": { label: "Permission deleted", tone: "rose" },
  "role-created": { label: "Role created", tone: "ink" },
  "role-deleted": { label: "Role deleted", tone: "rose" },
};

const PAGE_SIZE = 50;

export function IamAuditLogExperience() {
  const [data, setData] = useState<AuditLogPage | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [actor, setActor] = useState("");
  const [role, setRole] = useState("");
  const [action, setAction] = useState("");
  const [page, setPage] = useState(1);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const result = await queryAuditLog({
        actor: actor.trim() || undefined,
        role: role.trim() || undefined,
        action: action.trim() || undefined,
        page,
        pageSize: PAGE_SIZE,
      });
      setData(result);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setLoading(false);
    }
  }, [actor, role, action, page]);

  useEffect(() => {
    void load();
  }, [load]);

  // Filter changes always reset to page 1 — without this, a filter that
  // shrinks the result set below the current page leaves the table empty.
  function changeFilter(setter: (v: string) => void, value: string) {
    setter(value);
    setPage(1);
  }

  const totalPages = data ? Math.max(1, Math.ceil(data.totalCount / data.pageSize)) : 1;
  const hasFilters = actor || role || action;

  return (
    <div className="space-y-6">
      <header className="flex flex-col gap-2 sm:flex-row sm:items-end sm:justify-between">
        <div>
          <h1 className="text-[1.7rem] font-semibold text-[var(--color-ink-900)]">
            IAM audit log
          </h1>
          <p className="mt-1 text-[13px] text-[var(--color-ink-500)]">
            Append-only history of permission, role, and mapping changes.
            {data ? ` ${data.totalCount.toLocaleString()} total entries.` : null}
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

      {/* Filters */}
      <GlassCard className="flex flex-wrap items-end gap-3 px-4 py-3">
        <FilterInput
          label="Actor (EmployeeId)"
          value={actor}
          onChange={(v) => changeFilter(setActor, v)}
          placeholder="86347852"
        />
        <FilterInput
          label="Role"
          value={role}
          onChange={(v) => changeFilter(setRole, v)}
          placeholder="Admin"
        />
        <div>
          <label className="block text-[11.5px] font-medium uppercase tracking-wide text-[var(--color-ink-500)]">
            Action
          </label>
          <select
            value={action}
            onChange={(e) => changeFilter(setAction, e.target.value)}
            className="mt-1 rounded-lg border border-[var(--color-ink-100)] bg-white px-3 py-1.5 text-[12.5px] focus:outline-none focus:ring-2 focus:ring-[var(--color-ink-900)]/20"
          >
            <option value="">All actions</option>
            {Object.entries(ACTION_LABELS).map(([key, { label }]) => (
              <option key={key} value={key}>
                {label}
              </option>
            ))}
          </select>
        </div>
        {hasFilters ? (
          <button
            type="button"
            onClick={() => {
              setActor("");
              setRole("");
              setAction("");
              setPage(1);
            }}
            className="inline-flex h-[34px] items-center gap-1 rounded-full border border-[var(--color-ink-100)] bg-white px-3 text-[11.5px] font-medium text-[var(--color-ink-600)] hover:bg-[var(--color-ink-50)]"
          >
            <X className="h-3 w-3" />
            Clear filters
          </button>
        ) : null}
      </GlassCard>

      {/* Entries */}
      <GlassCard className="overflow-hidden p-0">
        <table className="w-full text-[13px]">
          <thead>
            <tr className="border-b border-[var(--color-ink-100)] text-left text-[11.5px] uppercase tracking-wide text-[var(--color-ink-500)]">
              <th className="px-4 py-2 font-medium">When</th>
              <th className="px-4 py-2 font-medium">Actor</th>
              <th className="px-4 py-2 font-medium">Action</th>
              <th className="px-4 py-2 font-medium">Role</th>
              <th className="px-4 py-2 font-medium">Permission</th>
            </tr>
          </thead>
          <tbody>
            {data?.items.length === 0 && !loading ? (
              <tr>
                <td colSpan={5} className="px-6 py-10 text-center text-[13px] text-[var(--color-ink-500)]">
                  No audit entries match the current filters.
                </td>
              </tr>
            ) : null}
            {data?.items.map((entry) => (
              <AuditRow key={entry.id} entry={entry} />
            ))}
          </tbody>
        </table>
      </GlassCard>

      {/* Pagination */}
      {data && data.totalCount > data.pageSize ? (
        <div className="flex items-center justify-between text-[12px] text-[var(--color-ink-500)]">
          <span>
            Page {data.page} of {totalPages}
          </span>
          <div className="flex items-center gap-1">
            <button
              type="button"
              onClick={() => setPage((p) => Math.max(1, p - 1))}
              disabled={page <= 1}
              className="rounded-full border border-[var(--color-ink-100)] bg-white px-3 py-1 text-[12px] hover:bg-[var(--color-ink-50)] disabled:cursor-not-allowed disabled:opacity-50"
            >
              Previous
            </button>
            <button
              type="button"
              onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
              disabled={page >= totalPages}
              className="rounded-full border border-[var(--color-ink-100)] bg-white px-3 py-1 text-[12px] hover:bg-[var(--color-ink-50)] disabled:cursor-not-allowed disabled:opacity-50"
            >
              Next
            </button>
          </div>
        </div>
      ) : null}
    </div>
  );
}

function AuditRow({ entry }: { entry: AuditLogEntryDto }) {
  const meta = ACTION_LABELS[entry.action] ?? { label: entry.action, tone: "ink" as const };
  return (
    <tr className="border-b border-[var(--color-ink-100)]/60 last:border-b-0 hover:bg-white/40">
      <td className="px-4 py-2 text-[12px] text-[var(--color-ink-700)]">
        <DateTime value={entry.occurredAt} />
      </td>
      <td className="px-4 py-2 font-mono text-[12px] text-[var(--color-ink-900)]">
        {entry.actorEmployeeId}
      </td>
      <td className="px-4 py-2">
        <span className={cn("inline-flex rounded-full px-2 py-0.5 text-[11px] font-medium", toneClass(meta.tone))}>
          {meta.label}
        </span>
      </td>
      <td className="px-4 py-2 text-[var(--color-ink-700)]">{entry.role ?? "—"}</td>
      <td className="px-4 py-2 font-mono text-[12px] text-[var(--color-ink-700)]">
        {entry.permissionCode ?? "—"}
      </td>
    </tr>
  );
}

function FilterInput({
  label,
  value,
  onChange,
  placeholder,
}: {
  label: string;
  value: string;
  onChange: (v: string) => void;
  placeholder: string;
}) {
  return (
    <div>
      <label className="block text-[11.5px] font-medium uppercase tracking-wide text-[var(--color-ink-500)]">
        {label}
      </label>
      <input
        type="text"
        value={value}
        onChange={(e) => onChange(e.target.value)}
        placeholder={placeholder}
        className="mt-1 rounded-lg border border-[var(--color-ink-100)] bg-white px-3 py-1.5 text-[12.5px] focus:outline-none focus:ring-2 focus:ring-[var(--color-ink-900)]/20"
      />
    </div>
  );
}

function toneClass(tone: "emerald" | "rose" | "ink" | "amber"): string {
  switch (tone) {
    case "emerald":
      return "bg-emerald-100 text-emerald-800";
    case "rose":
      return "bg-rose-100 text-rose-800";
    case "amber":
      return "bg-amber-100 text-amber-800";
    case "ink":
    default:
      return "bg-[var(--color-ink-100)] text-[var(--color-ink-700)]";
  }
}
