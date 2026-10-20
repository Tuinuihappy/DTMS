"use client";

import { AlertCircle, RefreshCw, X } from "lucide-react";
import { useCallback, useEffect, useRef, useState } from "react";
import {
  listAuditActions,
  queryAuditLog,
  type AuditLogEntryDto,
  type AuditLogPage,
} from "@/lib/api/iam";
import { listSystems } from "@/lib/api/iam-systems";
import { GlassCard } from "@/components/primitives/glass-card";
import { DateTime } from "@/components/primitives/date-time";
import { cn } from "@/lib/utils";

type Tone = "success" | "coral" | "amber" | "ink";

// Readable names for the actions the backend writes. The filter's options
// come from the log itself (listAuditActions); this map only labels them,
// and an action missing here still shows, humanised. It used to BE the
// option list — role/permission actions that no longer exist — so every
// choice returned nothing (frontend review M3).
const ACTION_META: Record<string, { label: string; tone: Tone }> = {
  "system-created": { label: "System created", tone: "success" },
  "system-updated": { label: "System updated", tone: "amber" },
  "system-activated": { label: "System activated", tone: "success" },
  "system-deactivated": { label: "System deactivated", tone: "coral" },
  "system-hard-deleted": { label: "System deleted", tone: "coral" },
  "system-grant": { label: "Permission granted", tone: "success" },
  "system-revoke": { label: "Permission revoked", tone: "coral" },
  "system-credential-rotated": { label: "Credential rotated", tone: "amber" },
  "system-token-issued": { label: "Token issued", tone: "ink" },
  "system-token-revoked": { label: "Token revoked", tone: "coral" },
  "system-token-refreshed": { label: "Outbound token refreshed", tone: "ink" },
  "system-token-refresh-configured": { label: "Token refresh configured", tone: "amber" },
  "system-callback-updated": { label: "Callback updated", tone: "amber" },
  "system-callback-token-revealed": { label: "Callback token revealed", tone: "coral" },
  "subscription-created": { label: "Subscription created", tone: "success" },
  "subscription-updated": { label: "Subscription updated", tone: "amber" },
  "subscription-deleted": { label: "Subscription deleted", tone: "coral" },
  // Rows from before user roles moved to the auth service (2026-08-01).
  grant: { label: "Grant (legacy)", tone: "ink" },
  revoke: { label: "Revoke (legacy)", tone: "ink" },
  "role-created": { label: "Role created (legacy)", tone: "ink" },
  "role-deleted": { label: "Role deleted (legacy)", tone: "ink" },
};

function actionMeta(action: string): { label: string; tone: Tone } {
  return (
    ACTION_META[action] ?? {
      label: action.replace(/-/g, " ").replace(/^\w/, (c) => c.toUpperCase()),
      tone: "ink",
    }
  );
}

const PAGE_SIZE = 50;

export function IamAuditLogExperience() {
  const [data, setData] = useState<AuditLogPage | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  // Typed filters are debounced: a request per keystroke meant 8 requests
  // for an 8-digit EmployeeId, answering in any order (review M2).
  const [actorInput, setActorInput] = useState("");
  const [systemInput, setSystemInput] = useState("");
  const actor = useDebouncedValue(actorInput.trim(), 350);
  const systemKey = useDebouncedValue(systemInput.trim(), 350);
  const [action, setAction] = useState("");
  const [page, setPage] = useState(1);
  const [actions, setActions] = useState<string[]>([]);
  const [systemKeys, setSystemKeys] = useState<string[]>([]);

  // Only the newest load may write — and it aborts the one before it.
  const seq = useRef(0);
  const inFlight = useRef<AbortController | null>(null);

  const load = useCallback(async () => {
    const mine = ++seq.current;
    inFlight.current?.abort();
    const controller = new AbortController();
    inFlight.current = controller;
    setLoading(true);
    setError(null);
    try {
      const result = await queryAuditLog({
        actor: actor || undefined,
        action: action || undefined,
        systemKey: systemKey || undefined,
        page,
        pageSize: PAGE_SIZE,
        signal: controller.signal,
      });
      if (mine === seq.current) setData(result);
    } catch (e) {
      if (mine === seq.current && (e as Error).name !== "AbortError") setError((e as Error).message);
    } finally {
      if (mine === seq.current) setLoading(false);
    }
  }, [actor, action, systemKey, page]);

  useEffect(() => {
    void load();
  }, [load]);

  useEffect(() => () => inFlight.current?.abort(), []);

  // Filter options: the actions in the log, and the systems that exist
  // (free text still allowed — a deleted system's history stays searchable).
  useEffect(() => {
    const controller = new AbortController();
    listAuditActions(controller.signal)
      .then(setActions)
      .catch(() => setActions(Object.keys(ACTION_META)));
    listSystems(controller.signal)
      .then((s) => setSystemKeys(s.map((x) => x.key).sort()))
      .catch(() => undefined);
    return () => controller.abort();
  }, []);

  // Any filter change goes back to page 1 — a smaller result set would
  // otherwise leave the table on an empty page.
  useEffect(() => {
    setPage(1);
  }, [actor, systemKey, action]);

  const totalPages = data ? Math.max(1, Math.ceil(data.totalCount / data.pageSize)) : 1;
  const hasFilters = actorInput || systemInput || action;

  return (
    <div className="space-y-6">
      <header className="flex flex-col gap-2 sm:flex-row sm:items-end sm:justify-between">
        <div>
          <h1 className="text-[1.7rem] font-semibold text-[var(--color-ink-900)]">
            IAM audit log
          </h1>
          <p className="mt-1 text-[13px] text-[var(--color-ink-500)]">
            Append-only history of system-client changes — credentials, tokens,
            callbacks, permissions and subscriptions.
            {data ? ` ${data.totalCount.toLocaleString()} matching entries.` : null}
          </p>
        </div>
        <button
          type="button"
          onClick={() => void load()}
          className="inline-flex items-center gap-1.5 rounded-full border border-[var(--color-ink-100)] bg-white/70 px-3 py-1.5 text-[11.5px] font-medium text-[var(--color-ink-600)] hover:bg-white dark:bg-white/[0.05] dark:hover:bg-white/[0.12]"
        >
          <RefreshCw className={cn("h-3 w-3", loading && "animate-spin")} strokeWidth={2.4} />
          Refresh
        </button>
      </header>

      {error ? (
        <GlassCard className="flex items-center gap-2 border-[var(--color-coral)]/30 bg-[var(--color-coral-soft)]/60 px-4 py-3 text-[13px] text-[var(--color-coral)]">
          <AlertCircle className="h-4 w-4" />
          {error}
        </GlassCard>
      ) : null}

      {/* Filters */}
      <GlassCard className="flex flex-wrap items-end gap-3 px-4 py-3">
        <FilterInput
          label="System"
          value={systemInput}
          onChange={setSystemInput}
          placeholder="oms"
          suggestions={systemKeys}
        />
        <FilterInput
          label="Actor (EmployeeId)"
          value={actorInput}
          onChange={setActorInput}
          placeholder="86347852"
        />
        <div>
          <label
            htmlFor="audit-action"
            className="block text-[11.5px] font-medium uppercase tracking-wide text-[var(--color-ink-500)]"
          >
            Action
          </label>
          <select
            id="audit-action"
            value={action}
            onChange={(e) => setAction(e.target.value)}
            className="mt-1 rounded-lg border border-[var(--color-ink-100)] bg-white px-3 py-1.5 text-[12.5px] text-[var(--color-ink-900)] focus:outline-none focus:ring-2 focus:ring-[var(--color-ink-900)]/20 dark:bg-white/[0.04]"
          >
            <option value="">All actions</option>
            {actions.map((key) => (
              <option key={key} value={key}>
                {actionMeta(key).label}
              </option>
            ))}
          </select>
        </div>
        {hasFilters ? (
          <button
            type="button"
            onClick={() => {
              setActorInput("");
              setSystemInput("");
              setAction("");
            }}
            className="inline-flex h-[34px] items-center gap-1 rounded-full border border-[var(--color-ink-100)] bg-white px-3 text-[11.5px] font-medium text-[var(--color-ink-600)] hover:bg-[var(--color-ink-50)] dark:bg-white/[0.04]"
          >
            <X className="h-3 w-3" />
            Clear filters
          </button>
        ) : null}
      </GlassCard>

      {/* Entries */}
      <GlassCard className="overflow-hidden p-0">
        <div className="overflow-x-auto">
          <table className="w-full text-[13px]">
            <thead>
              <tr className="border-b border-[var(--color-ink-100)] text-left text-[11.5px] uppercase tracking-wide text-[var(--color-ink-500)]">
                <th className="px-4 py-2 font-medium">When</th>
                <th className="px-4 py-2 font-medium">Actor</th>
                <th className="px-4 py-2 font-medium">Action</th>
                <th className="px-4 py-2 font-medium">System</th>
                <th className="px-4 py-2 font-medium">Details</th>
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
                <AuditRow key={entry.id} entry={entry} onSystem={setSystemInput} />
              ))}
            </tbody>
          </table>
        </div>
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
              className="rounded-full border border-[var(--color-ink-100)] bg-white px-3 py-1 text-[12px] hover:bg-[var(--color-ink-50)] disabled:cursor-not-allowed disabled:opacity-50 dark:bg-white/[0.04]"
            >
              Previous
            </button>
            <button
              type="button"
              onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
              disabled={page >= totalPages}
              className="rounded-full border border-[var(--color-ink-100)] bg-white px-3 py-1 text-[12px] hover:bg-[var(--color-ink-50)] disabled:cursor-not-allowed disabled:opacity-50 dark:bg-white/[0.04]"
            >
              Next
            </button>
          </div>
        </div>
      ) : null}
    </div>
  );
}

// Details is compact JSON written by the API. The system goes in its own
// column; the rest becomes "key: value" pairs. Before this the column did
// not exist and every system row read "— —" (frontend review M1).
function parseDetails(details: string | null): { systemKey: string | null; pairs: [string, string][] } {
  if (!details) return { systemKey: null, pairs: [] };
  try {
    const obj = JSON.parse(details) as Record<string, unknown>;
    const systemKey = typeof obj.systemKey === "string" ? obj.systemKey : null;
    const pairs = Object.entries(obj)
      .filter(([k, v]) => k !== "systemKey" && v !== null && v !== undefined && v !== "")
      .map(([k, v]) => [k, typeof v === "object" ? JSON.stringify(v) : String(v)] as [string, string]);
    return { systemKey, pairs };
  } catch {
    return { systemKey: null, pairs: [["details", details]] };
  }
}

function AuditRow({
  entry,
  onSystem,
}: {
  entry: AuditLogEntryDto;
  onSystem: (key: string) => void;
}) {
  const meta = actionMeta(entry.action);
  const { systemKey, pairs } = parseDetails(entry.details);
  // Legacy rows keep role / permission columns that system rows lack.
  if (entry.permissionCode) pairs.unshift(["permission", entry.permissionCode]);
  if (entry.role) pairs.unshift(["role", entry.role]);
  return (
    <tr className="border-b border-[var(--color-ink-100)]/60 align-top last:border-b-0 hover:bg-white/40 dark:hover:bg-white/[0.03]">
      <td className="whitespace-nowrap px-4 py-2 text-[12px] text-[var(--color-ink-700)]">
        <DateTime value={entry.occurredAt} />
      </td>
      <td className="px-4 py-2 font-mono text-[12px] text-[var(--color-ink-900)]">
        {entry.actorEmployeeId}
      </td>
      <td className="px-4 py-2">
        <span
          className={cn("inline-flex whitespace-nowrap rounded-full px-2 py-0.5 text-[11px] font-medium", toneClass(meta.tone))}
          title={entry.action}
        >
          {meta.label}
        </span>
      </td>
      <td className="px-4 py-2">
        {systemKey ? (
          <button
            type="button"
            onClick={() => onSystem(systemKey)}
            className="rounded-full bg-[var(--color-ink-100)] px-2 py-0.5 font-mono text-[11.5px] text-[var(--color-ink-800)] hover:bg-[var(--color-ink-200)]"
            title={`Show only ${systemKey}`}
          >
            {systemKey}
          </button>
        ) : (
          <span className="text-[var(--color-ink-400)]">—</span>
        )}
      </td>
      <td className="px-4 py-2 text-[12px] text-[var(--color-ink-700)]">
        {pairs.length === 0 ? (
          <span className="text-[var(--color-ink-400)]">—</span>
        ) : (
          <dl className="flex flex-wrap gap-x-3 gap-y-0.5">
            {pairs.map(([k, v]) => (
              <div key={k} className="flex gap-1">
                <dt className="text-[var(--color-ink-500)]">{k}:</dt>
                <dd className="break-all font-mono text-[11.5px] text-[var(--color-ink-800)]">{v}</dd>
              </div>
            ))}
          </dl>
        )}
      </td>
    </tr>
  );
}

function FilterInput({
  label,
  value,
  onChange,
  placeholder,
  suggestions,
}: {
  label: string;
  value: string;
  onChange: (v: string) => void;
  placeholder: string;
  suggestions?: string[];
}) {
  const id = `audit-filter-${label.toLowerCase().replace(/\W+/g, "-")}`;
  return (
    <div>
      <label
        htmlFor={id}
        className="block text-[11.5px] font-medium uppercase tracking-wide text-[var(--color-ink-500)]"
      >
        {label}
      </label>
      <input
        id={id}
        type="text"
        value={value}
        onChange={(e) => onChange(e.target.value)}
        placeholder={placeholder}
        list={suggestions ? `${id}-list` : undefined}
        autoComplete="off"
        className="mt-1 rounded-lg border border-[var(--color-ink-100)] bg-white px-3 py-1.5 text-[12.5px] text-[var(--color-ink-900)] focus:outline-none focus:ring-2 focus:ring-[var(--color-ink-900)]/20 dark:bg-white/[0.04]"
      />
      {suggestions && (
        <datalist id={`${id}-list`}>
          {suggestions.map((s) => (
            <option key={s} value={s} />
          ))}
        </datalist>
      )}
    </div>
  );
}

function toneClass(tone: Tone): string {
  switch (tone) {
    case "success":
      return "bg-[var(--color-success-soft)] text-[var(--color-success)]";
    case "coral":
      return "bg-[var(--color-coral-soft)] text-[var(--color-coral)]";
    case "amber":
      return "bg-[var(--color-amber-soft)] text-[var(--color-amber-600)]";
    case "ink":
    default:
      return "bg-[var(--color-ink-100)] text-[var(--color-ink-700)]";
  }
}

function useDebouncedValue<T>(value: T, delay: number): T {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => {
    const t = window.setTimeout(() => setDebounced(value), delay);
    return () => window.clearTimeout(t);
  }, [value, delay]);
  return debounced;
}
