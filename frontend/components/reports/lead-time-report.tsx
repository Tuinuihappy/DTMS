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
  getLeadTimeReport,
  ordersReportCsvUrl,
  type Window,
} from "@/lib/api/reports";
import { useProjectionPoll } from "@/lib/hooks/use-projection-poll";
import { cn } from "@/lib/utils";
import { fmtDuration } from "./window-toggle";

export function LeadTimeReport({ window }: { window: Window }) {
  const fetcher = useCallback(
    (signal: AbortSignal) => getLeadTimeReport(window, signal),
    [window],
  );
  const { data, loading, error, refresh } = useProjectionPoll(fetcher, {
    intervalMs: 5 * 60_000,
  });

  const csvHref = ordersReportCsvUrl(window);
  const chartData = (data?.buckets ?? []).map((b) => ({
    label: b.label,
    count: b.count,
  }));

  return (
    <div className="space-y-4">
      <section className="grid grid-cols-2 md:grid-cols-4 gap-3">
        <Tile label="Completed orders" value={(data?.totalCompleted ?? 0).toLocaleString()} />
        <Tile label="Average" value={fmtDuration(data?.avgSec ?? null)} />
        <Tile label="Median (p50)" value={fmtDuration(data?.p50Sec ?? null)} />
        <Tile label="P95" value={fmtDuration(data?.p95Sec ?? null)} tone="amber" />
      </section>

      <GlassCard variant="default" className="p-5">
        <SectionLabel
          title="Lead-time distribution"
          subtitle="Time from CreatedAt to CompletedAt, bucketed for the trailing window."
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
          <div className="mt-4 rounded-xl bg-[var(--color-coral-soft)] px-3 py-2 text-[11.5px] font-medium text-[var(--color-coral)]">
            {error}
          </div>
        )}

        <div className="mt-4 h-64">
          <ChartMount>
          <ResponsiveContainer width="100%" height="100%">
            <BarChart data={chartData} margin={{ top: 10, right: 10, left: 0, bottom: 10 }}>
              <CartesianGrid strokeDasharray="3 3" stroke="var(--color-ink-100)" />
              <XAxis dataKey="label" tick={{ fontSize: 11 }} />
              <YAxis tick={{ fontSize: 11 }} allowDecimals={false} />
              <Tooltip />
              <Bar dataKey="count" name="Orders" fill="var(--color-brand-500, #6366f1)" />
            </BarChart>
          </ResponsiveContainer>
          </ChartMount>
        </div>

        <div className="mt-4 overflow-x-auto">
          <table className="w-full border-collapse text-[12.5px]">
            <thead>
              <tr className="text-left text-[10.5px] font-semibold uppercase tracking-[0.1em] text-[var(--color-ink-400)]">
                <th className="px-2 py-2">Bucket</th>
                <th className="px-2 py-2 text-right">Count</th>
                <th className="px-2 py-2 text-right">% of total</th>
              </tr>
            </thead>
            <tbody>
              {(data?.buckets ?? []).map((b) => (
                <tr key={b.label} className="border-t border-[var(--color-ink-100)]/70 dark:border-white/5">
                  <td className="px-2 py-2 font-semibold">{b.label}</td>
                  <td className="px-2 py-2 text-right font-mono tabular-nums">{b.count.toLocaleString()}</td>
                  <td className="px-2 py-2 text-right font-mono tabular-nums">{(b.pct * 100).toFixed(1)}%</td>
                </tr>
              ))}
              {(data?.totalCompleted ?? 0) === 0 && (
                <tr>
                  <td colSpan={3} className="px-2 py-6 text-center text-[var(--color-ink-400)]">
                    {loading ? "Loading…" : "No completed orders in this window."}
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </GlassCard>
    </div>
  );
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
          "mt-2 font-mono text-[1.5rem] font-semibold tabular-nums leading-none",
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
