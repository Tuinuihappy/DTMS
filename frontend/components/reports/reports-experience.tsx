"use client";

import { Download, FileBarChart2, RefreshCw } from "lucide-react";
import { useCallback, useMemo, useState } from "react";
import { GlassCard } from "@/components/primitives/glass-card";
import { SectionLabel } from "@/components/primitives/section-label";
import {
  getOrdersReportSummary,
  ordersReportCsvUrl,
  type OrdersReportCell,
} from "@/lib/api/reports";
import { useProjectionPoll } from "@/lib/hooks/use-projection-poll";
import { cn } from "@/lib/utils";

// Phase P5.1 — Reports landing page. First template (Orders by
// Priority/Status) is wired end-to-end against the bi.OrderFacts
// projection. P5.3 adds the remaining 4 templates (SLA breach, top
// failures, vendor perf, lead-time distribution).

type Window = "24h" | "7d" | "30d" | "90d";

const WINDOW_HOURS: Record<Window, number> = {
  "24h": 24,
  "7d": 24 * 7,
  "30d": 24 * 30,
  "90d": 24 * 90,
};

function buildWindow(w: Window) {
  const now = new Date();
  const to = new Date(
    Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate(), now.getUTCHours() + 1),
  );
  const from = new Date(to.getTime() - WINDOW_HOURS[w] * 60 * 60 * 1000);
  return { fromUtc: from.toISOString(), toUtc: to.toISOString() };
}

export function ReportsExperience() {
  const [window, setWindow] = useState<Window>("7d");
  const filters = useMemo(() => buildWindow(window), [window]);

  const fetcher = useCallback(
    (signal: AbortSignal) => getOrdersReportSummary(filters, signal),
    [filters],
  );
  // Reports aren't real-time — poll only every 5 minutes so the freshness
  // chip ticks but we don't pound the DB on idle tabs.
  const { data, loading, error, refresh } = useProjectionPoll(fetcher, {
    intervalMs: 5 * 60_000,
  });

  const csvHref = ordersReportCsvUrl(filters);

  // Pivot the cell list into (Priority × FinalStatus) so the rendered
  // table mirrors what an analyst would build in a pivot table.
  const { priorities, statuses, matrix } = useMemo(
    () => pivot(data?.cells ?? []),
    [data?.cells],
  );

  return (
    <div className="space-y-6">
      <header className="flex flex-col gap-2 sm:flex-row sm:items-end sm:justify-between">
        <div>
          <h1 className="text-[1.7rem] font-semibold text-[var(--color-ink-900)]">
            Reports
          </h1>
          <p className="mt-1 text-[13px] text-[var(--color-ink-500)]">
            Pre-built reports backed by the OrderFacts projection. Export to CSV
            for follow-up analysis.
          </p>
        </div>
        <div className="flex items-center gap-1.5">
          <WindowToggle value={window} onChange={setWindow} />
          <a
            href={csvHref}
            className="inline-flex items-center gap-1.5 rounded-full border border-[var(--color-ink-100)] bg-white/70 px-3 py-1.5 text-[11.5px] font-medium text-[var(--color-ink-600)] hover:bg-white dark:bg-white/[0.05] dark:hover:bg-white/[0.12]"
            aria-label="Export CSV"
          >
            <Download className="h-3 w-3" strokeWidth={2.4} />
            CSV
          </a>
          <button
            type="button"
            onClick={refresh}
            className="inline-flex items-center gap-1.5 rounded-full border border-[var(--color-ink-100)] bg-white/70 px-3 py-1.5 text-[11.5px] font-medium text-[var(--color-ink-600)] hover:bg-white cursor-pointer dark:bg-white/[0.05] dark:hover:bg-white/[0.12]"
            aria-label="Refresh"
          >
            <RefreshCw
              className={cn("h-3 w-3", loading && "animate-spin")}
              strokeWidth={2.4}
            />
            Refresh
          </button>
        </div>
      </header>

      {/* Headline totals */}
      <section className="grid grid-cols-2 md:grid-cols-4 gap-3">
        <Tile label="Orders" value={data?.totalOrders ?? 0} />
        <Tile
          label="SLA confirm breach"
          value={(data?.cells ?? []).reduce((a, c) => a + c.slaConfirmBreached, 0)}
          tone="amber"
        />
        <Tile
          label="SLA complete breach"
          value={(data?.cells ?? []).reduce((a, c) => a + c.slaCompleteBreached, 0)}
          tone="coral"
        />
        <Tile
          label="Avg lead time"
          value={avgLeadTime(data?.cells ?? [])}
          formatter={fmtDuration}
        />
      </section>

      <GlassCard variant="default" className="p-5">
        <SectionLabel
          title="Orders by priority × final status"
          subtitle={`Trailing ${window} · grouped from bi.OrderFacts`}
          action={
            <span className="inline-flex items-center gap-1.5 rounded-full border border-[var(--color-ink-100)] bg-white/70 px-3 py-1.5 text-[11.5px] font-medium text-[var(--color-ink-600)] dark:bg-white/[0.05]">
              <FileBarChart2 className="h-3 w-3" strokeWidth={2.4} />
              Template 1
            </span>
          }
        />

        {error && (
          <div className="mt-4 rounded-xl bg-[#fde0db] px-3 py-2 text-[11.5px] font-medium text-[var(--color-coral)] dark:bg-[#3a1a17]">
            {error}
          </div>
        )}

        <div className="mt-4 overflow-x-auto">
          <table className="w-full border-collapse text-[12.5px]">
            <thead>
              <tr className="text-left text-[10.5px] font-semibold uppercase tracking-[0.1em] text-[var(--color-ink-400)]">
                <th className="px-2 py-2">Priority</th>
                {statuses.map((s) => (
                  <th key={s} className="px-2 py-2 text-right">
                    {s}
                  </th>
                ))}
                <th className="px-2 py-2 text-right">Total</th>
              </tr>
            </thead>
            <tbody>
              {priorities.length === 0 && (
                <tr>
                  <td
                    colSpan={statuses.length + 2}
                    className="px-2 py-6 text-center text-[var(--color-ink-400)]"
                  >
                    {loading ? "Loading…" : "No orders in this window."}
                  </td>
                </tr>
              )}
              {priorities.map((p) => {
                const rowTotal = statuses.reduce(
                  (a, s) => a + (matrix[p]?.[s]?.count ?? 0),
                  0,
                );
                return (
                  <tr
                    key={p}
                    className="border-t border-[var(--color-ink-100)]/70 dark:border-white/5"
                  >
                    <td className="px-2 py-2 font-semibold text-[var(--color-ink-800)] dark:text-[var(--color-ink-100)]">
                      {p}
                    </td>
                    {statuses.map((s) => {
                      const cell = matrix[p]?.[s];
                      return (
                        <td
                          key={s}
                          className="px-2 py-2 text-right font-mono tabular-nums text-[var(--color-ink-700)]"
                        >
                          {cell ? cell.count.toLocaleString() : "—"}
                        </td>
                      );
                    })}
                    <td className="px-2 py-2 text-right font-mono font-semibold tabular-nums text-[var(--color-ink-900)]">
                      {rowTotal.toLocaleString()}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      </GlassCard>
    </div>
  );
}

function pivot(cells: OrdersReportCell[]) {
  const priorities = Array.from(new Set(cells.map((c) => c.priority))).sort();
  const statuses = Array.from(new Set(cells.map((c) => c.finalStatus))).sort();
  const matrix: Record<string, Record<string, OrdersReportCell>> = {};
  for (const c of cells) {
    matrix[c.priority] ??= {};
    matrix[c.priority][c.finalStatus] = c;
  }
  return { priorities, statuses, matrix };
}

function avgLeadTime(cells: OrdersReportCell[]): number {
  const valid = cells.filter((c) => c.avgTimeToCompleteSec != null);
  if (valid.length === 0) return 0;
  const weighted = valid.reduce(
    (a, c) => a + (c.avgTimeToCompleteSec ?? 0) * c.count,
    0,
  );
  const totalCount = valid.reduce((a, c) => a + c.count, 0);
  return totalCount > 0 ? weighted / totalCount : 0;
}

function fmtDuration(sec: number): string {
  if (sec <= 0) return "—";
  if (sec < 60) return `${sec.toFixed(0)}s`;
  if (sec < 3600) return `${(sec / 60).toFixed(1)}m`;
  if (sec < 86400) return `${(sec / 3600).toFixed(1)}h`;
  return `${(sec / 86400).toFixed(1)}d`;
}

function Tile({
  label,
  value,
  tone = "ink",
  formatter,
}: {
  label: string;
  value: number;
  tone?: "ink" | "amber" | "coral";
  formatter?: (v: number) => string;
}) {
  const display = formatter ? formatter(value) : value.toLocaleString();
  return (
    <GlassCard variant="default" className="p-4">
      <div className="text-[10.5px] font-semibold uppercase tracking-[0.12em] text-[var(--color-ink-500)]">
        {label}
      </div>
      <div
        className={cn(
          "mt-2 font-mono text-[1.8rem] font-semibold tabular-nums leading-none",
          tone === "amber"
            ? "text-[var(--color-amber-600)]"
            : tone === "coral"
              ? "text-[var(--color-coral)]"
              : "text-[var(--color-ink-900)]",
        )}
      >
        {display}
      </div>
    </GlassCard>
  );
}

function WindowToggle({
  value,
  onChange,
}: {
  value: Window;
  onChange: (w: Window) => void;
}) {
  const options: Window[] = ["24h", "7d", "30d", "90d"];
  return (
    <div className="inline-flex rounded-full bg-[var(--color-surface-soft)] p-1 dark:bg-white/[0.04]">
      {options.map((opt) => (
        <button
          key={opt}
          type="button"
          onClick={() => onChange(opt)}
          className={cn(
            "rounded-full px-3 py-1 text-[11px] font-semibold uppercase tracking-[0.06em] transition-colors",
            value === opt
              ? "bg-[var(--color-brand-900)] text-white dark:bg-[var(--color-brand-500)] dark:text-[var(--color-ink-50)]"
              : "text-[var(--color-ink-500)] hover:text-[var(--color-ink-900)]",
          )}
        >
          {opt}
        </button>
      ))}
    </div>
  );
}
