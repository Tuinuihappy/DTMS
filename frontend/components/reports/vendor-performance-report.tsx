"use client";

import { Download, RefreshCw } from "lucide-react";
import { useCallback } from "react";
import {
  Bar,
  BarChart,
  CartesianGrid,
  Legend,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";
import { ChartMount } from "@/components/primitives/chart-mount";
import { GlassCard } from "@/components/primitives/glass-card";
import { SectionLabel } from "@/components/primitives/section-label";
import {
  getVendorPerformanceReport,
  tripsExportCsvUrl,
  type Window,
} from "@/lib/api/reports";
import { useProjectionPoll } from "@/lib/hooks/use-projection-poll";
import { cn } from "@/lib/utils";
import { fmtDuration } from "./window-toggle";

export function VendorPerformanceReport({ window }: { window: Window }) {
  const fetcher = useCallback(
    (signal: AbortSignal) => getVendorPerformanceReport(window, signal),
    [window],
  );
  const { data, loading, error, refresh } = useProjectionPoll(fetcher, {
    intervalMs: 5 * 60_000,
  });

  const csvHref = tripsExportCsvUrl(window);
  const chartData = (data?.rows ?? []).slice(0, 10).map((r) => ({
    vendor: r.vendorUpperKey,
    completed: r.completed,
    failed: r.failed,
    cancelled: r.cancelled,
  }));

  return (
    <div className="space-y-4">
      <section className="grid grid-cols-2 md:grid-cols-3 gap-3">
        <Tile label="Total trips" value={(data?.totalTrips ?? 0).toLocaleString()} />
        <Tile label="Vendors" value={(data?.rows.length ?? 0).toLocaleString()} />
        <Tile
          label="Best success rate"
          value={
            data && data.rows.length > 0
              ? `${data.rows[0].vendorUpperKey} (${(
                  Math.max(...data.rows.map((r) => r.successRate)) * 100
                ).toFixed(1)}%)`
              : "—"
          }
        />
      </section>

      <GlassCard variant="default" className="p-5">
        <SectionLabel
          title="Trips by vendor (top 10)"
          subtitle="Stacked by outcome — Completed / Failed / Cancelled. Sorted by trip volume."
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

        <div className="mt-4 h-72">
          <ChartMount>
          <ResponsiveContainer width="100%" height="100%">
            <BarChart data={chartData} margin={{ top: 10, right: 10, left: 0, bottom: 30 }}>
              <CartesianGrid strokeDasharray="3 3" stroke="var(--color-ink-100)" />
              <XAxis
                dataKey="vendor"
                tick={{ fontSize: 10 }}
                angle={-25}
                textAnchor="end"
                height={50}
                interval={0}
              />
              <YAxis tick={{ fontSize: 11 }} allowDecimals={false} />
              <Tooltip />
              <Legend wrapperStyle={{ fontSize: 11 }} />
              <Bar dataKey="completed" stackId="a" name="Completed" fill="var(--color-mint, #4ade80)" />
              <Bar dataKey="failed" stackId="a" name="Failed" fill="var(--color-coral, #f87171)" />
              <Bar dataKey="cancelled" stackId="a" name="Cancelled" fill="var(--color-ink-400, #94a3b8)" />
            </BarChart>
          </ResponsiveContainer>
          </ChartMount>
        </div>

        <div className="mt-4 overflow-x-auto">
          <table className="w-full border-collapse text-[12.5px]">
            <thead>
              <tr className="text-left text-[10.5px] font-semibold uppercase tracking-[0.1em] text-[var(--color-ink-400)]">
                <th className="px-2 py-2">Vendor</th>
                <th className="px-2 py-2 text-right">Trips</th>
                <th className="px-2 py-2 text-right">Completed</th>
                <th className="px-2 py-2 text-right">Failed</th>
                <th className="px-2 py-2 text-right">Cancelled</th>
                <th className="px-2 py-2 text-right">Success</th>
                <th className="px-2 py-2 text-right">Avg time</th>
                <th className="px-2 py-2 text-right">P95</th>
                <th className="px-2 py-2 text-right">SLA breach</th>
              </tr>
            </thead>
            <tbody>
              {(data?.rows ?? []).length === 0 && (
                <tr>
                  <td colSpan={9} className="px-2 py-6 text-center text-[var(--color-ink-400)]">
                    {loading ? "Loading…" : "No trips in this window."}
                  </td>
                </tr>
              )}
              {(data?.rows ?? []).map((r) => (
                <tr key={r.vendorUpperKey} className="border-t border-[var(--color-ink-100)]/70 dark:border-white/5">
                  <td className="px-2 py-2 font-semibold">{r.vendorUpperKey}</td>
                  <td className="px-2 py-2 text-right font-mono tabular-nums">{r.totalTrips.toLocaleString()}</td>
                  <td className="px-2 py-2 text-right font-mono tabular-nums">{r.completed.toLocaleString()}</td>
                  <td className="px-2 py-2 text-right font-mono tabular-nums">{r.failed.toLocaleString()}</td>
                  <td className="px-2 py-2 text-right font-mono tabular-nums">{r.cancelled.toLocaleString()}</td>
                  <td
                    className={cn(
                      "px-2 py-2 text-right font-mono tabular-nums",
                      r.successRate >= 0.95
                        ? "text-[var(--color-mint, #4ade80)]"
                        : r.successRate >= 0.8
                          ? "text-[var(--color-amber-600)]"
                          : "text-[var(--color-coral)]",
                    )}
                  >
                    {(r.successRate * 100).toFixed(1)}%
                  </td>
                  <td className="px-2 py-2 text-right font-mono tabular-nums">{fmtDuration(r.avgTimeToCompleteSec)}</td>
                  <td className="px-2 py-2 text-right font-mono tabular-nums">{fmtDuration(r.p95TimeToCompleteSec)}</td>
                  <td className="px-2 py-2 text-right font-mono tabular-nums text-[var(--color-coral)]">
                    {r.slaBreached.toLocaleString()}
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

function Tile({
  label,
  value,
}: {
  label: string;
  value: string;
}) {
  return (
    <GlassCard variant="default" className="p-4">
      <div className="text-[10.5px] font-semibold uppercase tracking-[0.12em] text-[var(--color-ink-500)]">
        {label}
      </div>
      <div className="mt-2 font-mono text-[1.3rem] font-semibold tabular-nums leading-tight text-[var(--color-ink-900)] truncate" title={value}>
        {value}
      </div>
    </GlassCard>
  );
}
