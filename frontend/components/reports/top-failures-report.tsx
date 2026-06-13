"use client";

import { Download, RefreshCw } from "lucide-react";
import { useCallback } from "react";
import {
  Bar,
  BarChart,
  CartesianGrid,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";
import { ChartMount } from "@/components/primitives/chart-mount";
import { GlassCard } from "@/components/primitives/glass-card";
import { SectionLabel } from "@/components/primitives/section-label";
import {
  getTopFailuresReport,
  ordersReportCsvUrl,
  type Window,
} from "@/lib/api/reports";
import { useProjectionPoll } from "@/lib/hooks/use-projection-poll";
import { cn } from "@/lib/utils";

export function TopFailuresReport({ window }: { window: Window }) {
  const fetcher = useCallback(
    (signal: AbortSignal) => getTopFailuresReport(window, 20, signal),
    [window],
  );
  const { data, loading, error, refresh } = useProjectionPoll(fetcher, {
    intervalMs: 5 * 60_000,
  });

  const csvHref = ordersReportCsvUrl(window);
  // Recharts horizontal bar: take top 10 for chart, full list in table.
  const chartData = (data?.rows ?? []).slice(0, 10).map((r) => ({
    label: truncate(r.reason, 28),
    count: r.count,
    finalStatus: r.finalStatus,
  }));

  return (
    <div className="space-y-4">
      <section className="grid grid-cols-2 md:grid-cols-3 gap-3">
        <Tile label="Failed orders" value={(data?.totalFailedOrders ?? 0).toLocaleString()} tone="coral" />
        <Tile label="Unique reasons" value={(data?.rows.length ?? 0).toLocaleString()} />
        <Tile
          label="Top reason"
          value={data?.rows[0]?.reason ?? "—"}
          hint={data?.rows[0] ? `${data.rows[0].count} order(s)` : undefined}
        />
      </section>

      <GlassCard variant="default" className="p-5">
        <SectionLabel
          title="Top 10 failure reasons"
          subtitle="Aggregated across Failed / Cancelled / Rejected / Held orders in this window."
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

        <div className="mt-4 h-80">
          <ChartMount>
          <ResponsiveContainer width="100%" height="100%">
            <BarChart
              data={chartData}
              layout="vertical"
              margin={{ top: 10, right: 20, left: 0, bottom: 10 }}
            >
              <CartesianGrid strokeDasharray="3 3" stroke="var(--color-ink-100)" />
              <XAxis type="number" tick={{ fontSize: 11 }} allowDecimals={false} />
              <YAxis
                type="category"
                dataKey="label"
                tick={{ fontSize: 11 }}
                width={210}
              />
              <Tooltip />
              <Bar dataKey="count" name="Count" fill="var(--color-coral, #f87171)" />
            </BarChart>
          </ResponsiveContainer>
          </ChartMount>
        </div>

        <div className="mt-4 overflow-x-auto">
          <table className="w-full border-collapse text-[12.5px]">
            <thead>
              <tr className="text-left text-[10.5px] font-semibold uppercase tracking-[0.1em] text-[var(--color-ink-400)]">
                <th className="px-2 py-2">Reason</th>
                <th className="px-2 py-2">Final status</th>
                <th className="px-2 py-2 text-right">Count</th>
                <th className="px-2 py-2 text-right">% of failures</th>
              </tr>
            </thead>
            <tbody>
              {(data?.rows ?? []).length === 0 && (
                <tr>
                  <td colSpan={4} className="px-2 py-6 text-center text-[var(--color-ink-400)]">
                    {loading ? "Loading…" : "No failures in this window."}
                  </td>
                </tr>
              )}
              {(data?.rows ?? []).map((r, idx) => (
                <tr key={`${r.reason}-${r.finalStatus}-${idx}`} className="border-t border-[var(--color-ink-100)]/70 dark:border-white/5">
                  <td className="px-2 py-2 text-[var(--color-ink-800)] dark:text-[var(--color-ink-100)]">
                    {r.reason}
                  </td>
                  <td className="px-2 py-2 text-[var(--color-ink-600)]">{r.finalStatus}</td>
                  <td className="px-2 py-2 text-right font-mono tabular-nums">{r.count.toLocaleString()}</td>
                  <td className="px-2 py-2 text-right font-mono tabular-nums">
                    {(r.pctOfFailures * 100).toFixed(1)}%
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </GlassCard>
    </div>
  );
}

function truncate(s: string, max: number) {
  return s.length <= max ? s : `${s.slice(0, max - 1)}…`;
}

function Tile({
  label,
  value,
  tone = "ink",
  hint,
}: {
  label: string;
  value: string;
  tone?: "ink" | "amber" | "coral";
  hint?: string;
}) {
  return (
    <GlassCard variant="default" className="p-4">
      <div className="text-[10.5px] font-semibold uppercase tracking-[0.12em] text-[var(--color-ink-500)]">
        {label}
      </div>
      <div
        className={cn(
          "mt-2 font-mono text-[1.3rem] font-semibold tabular-nums leading-tight truncate",
          tone === "amber"
            ? "text-[var(--color-amber-600)]"
            : tone === "coral"
              ? "text-[var(--color-coral)]"
              : "text-[var(--color-ink-900)]",
        )}
        title={value}
      >
        {value}
      </div>
      {hint && <div className="mt-1 text-[10.5px] text-[var(--color-ink-400)]">{hint}</div>}
    </GlassCard>
  );
}
