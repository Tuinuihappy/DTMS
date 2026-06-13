"use client";

import { Download, RefreshCw } from "lucide-react";
import { useCallback, useMemo } from "react";
import { GlassCard } from "@/components/primitives/glass-card";
import { SectionLabel } from "@/components/primitives/section-label";
import {
  getOrdersReportSummary,
  ordersReportCsvUrl,
  type OrdersReportCell,
  type Window,
} from "@/lib/api/reports";
import { useProjectionPoll } from "@/lib/hooks/use-projection-poll";
import { cn } from "@/lib/utils";
import { fmtDuration } from "./window-toggle";

// P5.1 template — Orders by Priority × FinalStatus.

export function OrdersSummaryReport({ window }: { window: Window }) {
  const fetcher = useCallback(
    (signal: AbortSignal) => getOrdersReportSummary(window, signal),
    [window],
  );
  const { data, loading, error, refresh } = useProjectionPoll(fetcher, {
    intervalMs: 5 * 60_000,
  });

  const csvHref = ordersReportCsvUrl(window);
  const { priorities, statuses, matrix } = useMemo(
    () => pivot(data?.cells ?? []),
    [data?.cells],
  );

  return (
    <div className="space-y-4">
      <section className="grid grid-cols-2 md:grid-cols-4 gap-3">
        <Tile label="Orders" value={(data?.totalOrders ?? 0).toLocaleString()} />
        <Tile
          label="SLA confirm breach"
          value={(data?.cells ?? []).reduce((a, c) => a + c.slaConfirmBreached, 0).toLocaleString()}
          tone="amber"
        />
        <Tile
          label="SLA complete breach"
          value={(data?.cells ?? []).reduce((a, c) => a + c.slaCompleteBreached, 0).toLocaleString()}
          tone="coral"
        />
        <Tile label="Avg lead time" value={fmtDuration(avgLeadTime(data?.cells ?? []))} />
      </section>

      <GlassCard variant="default" className="p-5">
        <SectionLabel
          title="Orders by priority × final status"
          subtitle="Group counts from bi.OrderFacts within the selected window."
          action={
            <div className="flex items-center gap-1.5">
              <a
                href={csvHref}
                className="inline-flex items-center gap-1.5 rounded-full border border-[var(--color-ink-100)] bg-white/70 px-3 py-1.5 text-[11.5px] font-medium text-[var(--color-ink-600)] hover:bg-white dark:bg-white/[0.05] dark:hover:bg-white/[0.12]"
              >
                <Download className="h-3 w-3" strokeWidth={2.4} />
                CSV
              </a>
              <button
                type="button"
                onClick={refresh}
                className="inline-flex items-center gap-1.5 rounded-full border border-[var(--color-ink-100)] bg-white/70 px-3 py-1.5 text-[11.5px] font-medium text-[var(--color-ink-600)] hover:bg-white cursor-pointer dark:bg-white/[0.05] dark:hover:bg-white/[0.12]"
              >
                <RefreshCw className={cn("h-3 w-3", loading && "animate-spin")} strokeWidth={2.4} />
                Refresh
              </button>
            </div>
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
                const rowTotal = statuses.reduce((a, s) => a + (matrix[p]?.[s]?.count ?? 0), 0);
                return (
                  <tr key={p} className="border-t border-[var(--color-ink-100)]/70 dark:border-white/5">
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
  const weighted = valid.reduce((a, c) => a + (c.avgTimeToCompleteSec ?? 0) * c.count, 0);
  const totalCount = valid.reduce((a, c) => a + c.count, 0);
  return totalCount > 0 ? weighted / totalCount : 0;
}

function Tile({
  label,
  value,
  tone = "ink",
}: {
  label: string;
  value: string;
  tone?: "ink" | "amber" | "coral";
}) {
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
        {value}
      </div>
    </GlassCard>
  );
}
