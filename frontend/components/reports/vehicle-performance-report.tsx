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
  getVehiclePerformanceReport,
  tripsExportCsvUrl,
  type Window,
} from "@/lib/api/reports";
import { useProjectionPoll } from "@/lib/hooks/use-projection-poll";
import { cn } from "@/lib/utils";
import {
  DataRow,
  DataTableBody,
  DataTableHead,
  TableSkeletonRows,
  TableTd,
  TableTh,
} from "@/components/primitives/data-table";
import { fmtDuration } from "./window-toggle";

export function VehiclePerformanceReport({ window }: { window: Window }) {
  const fetcher = useCallback(
    (signal: AbortSignal) => getVehiclePerformanceReport(window, signal),
    [window],
  );
  const { data, loading, error, refresh } = useProjectionPoll(fetcher, {
    intervalMs: 5 * 60_000,
  });

  const csvHref = tripsExportCsvUrl(window);
  const chartData = (data?.rows ?? []).slice(0, 10).map((r) => ({
    vehicle: r.vendorVehicleKey,
    completed: r.completed,
    failed: r.failed,
    cancelled: r.cancelled,
  }));
  // "(unassigned)" rows come from trips that never started — surface
  // them in the totals tile so ops notices, but pin the "best vehicle"
  // tile to a vehicle that actually completed something.
  const bestVehicle = (data?.rows ?? [])
    .filter((r) => r.vendorVehicleKey !== "(unassigned)" && r.completed + r.failed + r.cancelled > 0)
    .reduce<typeof data extends infer T ? T extends { rows: infer R } ? R extends Array<infer Row> ? Row | null : null : null : null>(
      (best, r) => (best === null || r.successRate > best.successRate ? r : best),
      null,
    );

  return (
    <div className="space-y-4">
      <section className="grid grid-cols-2 md:grid-cols-3 gap-3">
        <Tile label="Total trips" value={(data?.totalTrips ?? 0).toLocaleString("en-US")} />
        <Tile label="Vehicles" value={(data?.rows.length ?? 0).toLocaleString("en-US")} />
        <Tile
          label="Best success rate"
          value={
            bestVehicle
              ? `${bestVehicle.vendorVehicleKey} (${(bestVehicle.successRate * 100).toFixed(1)}%)`
              : "—"
          }
        />
      </section>

      <GlassCard variant="default" className="p-5">
        <SectionLabel
          title="Trips by vehicle (top 10)"
          subtitle="Group by VendorVehicleKey (RIOT3 deviceKey). Stacked by outcome — Completed / Failed / Cancelled. Sorted by trip volume."
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

        <div className="mt-4 h-72">
          <ChartMount>
          <ResponsiveContainer width="100%" height="100%">
            <BarChart data={chartData} margin={{ top: 10, right: 10, left: 0, bottom: 30 }}>
              <CartesianGrid strokeDasharray="3 3" stroke="var(--color-ink-100)" />
              <XAxis
                dataKey="vehicle"
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
          <table className="w-full text-left">
            <DataTableHead>
              <TableTh density="compact">Vehicle</TableTh>
              <TableTh density="compact" align="right">Trips</TableTh>
              <TableTh density="compact" align="right">Completed</TableTh>
              <TableTh density="compact" align="right">Failed</TableTh>
              <TableTh density="compact" align="right">Cancelled</TableTh>
              <TableTh density="compact" align="right">Success</TableTh>
              <TableTh density="compact" align="right">Avg time</TableTh>
              <TableTh density="compact" align="right">P95</TableTh>
              <TableTh density="compact" align="right">SLA breach</TableTh>
            </DataTableHead>
            <DataTableBody>
              {(data?.rows ?? []).length === 0 && loading && (
                <TableSkeletonRows colSpan={9} rows={3} />
              )}
              {(data?.rows ?? []).length === 0 && !loading && (
                <tr>
                  <TableTd
                    density="compact"
                    colSpan={9}
                    className="py-6 text-center text-[var(--color-ink-400)]"
                  >
                    No trips in this window.
                  </TableTd>
                </tr>
              )}
              {(data?.rows ?? []).map((r, i) => (
                <DataRow key={r.vendorVehicleKey} delayIndex={i}>
                  <TableTd density="compact" className="font-semibold">
                    {r.vendorVehicleKey}
                  </TableTd>
                  <TableTd density="compact" align="right" className="font-mono tabular-nums">
                    {r.totalTrips.toLocaleString("en-US")}
                  </TableTd>
                  <TableTd density="compact" align="right" className="font-mono tabular-nums">
                    {r.completed.toLocaleString("en-US")}
                  </TableTd>
                  <TableTd density="compact" align="right" className="font-mono tabular-nums">
                    {r.failed.toLocaleString("en-US")}
                  </TableTd>
                  <TableTd density="compact" align="right" className="font-mono tabular-nums">
                    {r.cancelled.toLocaleString("en-US")}
                  </TableTd>
                  <TableTd
                    density="compact"
                    align="right"
                    className={cn(
                      "font-mono tabular-nums",
                      r.successRate >= 0.95
                        ? "text-[var(--color-mint, #4ade80)]"
                        : r.successRate >= 0.8
                          ? "text-[var(--color-amber-600)]"
                          : "text-[var(--color-coral)]",
                    )}
                  >
                    {(r.successRate * 100).toFixed(1)}%
                  </TableTd>
                  <TableTd density="compact" align="right" className="font-mono tabular-nums">
                    {fmtDuration(r.avgTimeToCompleteSec)}
                  </TableTd>
                  <TableTd density="compact" align="right" className="font-mono tabular-nums">
                    {fmtDuration(r.p95TimeToCompleteSec)}
                  </TableTd>
                  <TableTd
                    density="compact"
                    align="right"
                    className="font-mono tabular-nums text-[var(--color-coral)]"
                  >
                    {r.slaBreached.toLocaleString("en-US")}
                  </TableTd>
                </DataRow>
              ))}
            </DataTableBody>
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
