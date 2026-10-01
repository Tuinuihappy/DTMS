"use client";

import { RefreshCw } from "lucide-react";
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
import { getManualOperatorsReport, type Window } from "@/lib/api/reports";
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

// Per-person breakdown of the vehicle report's "(manual)" row. The
// operator is the OMS user who first acknowledged the trip — not always
// the person who carried it, and some usernames are shared accounts.
export function ManualOperatorsReport({ window }: { window: Window }) {
  const fetcher = useCallback(
    (signal: AbortSignal) => getManualOperatorsReport(window, signal),
    [window],
  );
  const { data, loading, error, refresh } = useProjectionPoll(fetcher, {
    intervalMs: 5 * 60_000,
  });

  const rows = data?.rows ?? [];
  const chartData = rows.slice(0, 10).map((r) => ({
    operator: r.operator,
    completed: r.completed,
    failed: r.failed,
    rejected: r.rejected,
    cancelled: r.cancelled,
    inProgress: r.inProgress,
  }));
  // Rows arrive sorted by trip volume, so the first one is the busiest.
  const busiest = rows[0];

  return (
    <div className="space-y-4">
      <section className="grid grid-cols-2 md:grid-cols-3 gap-3">
        <Tile label="Total manual trips" value={(data?.totalTrips ?? 0).toLocaleString("en-US")} />
        <Tile label="Operators" value={rows.length.toLocaleString("en-US")} />
        <Tile
          label="Most trips"
          value={busiest ? `${busiest.operator} (${busiest.totalTrips.toLocaleString("en-US")})` : "—"}
        />
      </section>

      <GlassCard variant="default" className="p-5">
        <SectionLabel
          title="Manual trips by operator (top 10)"
          subtitle="Grouped by the OMS user who accepted the trip (first acknowledge). Shared accounts such as “all new” / “stf” appear as one operator. Stacked by outcome — Completed / Failed / Rejected / Cancelled / In progress. Sorted by trip volume."
          action={
            <button
              type="button"
              onClick={refresh}
              className="inline-flex items-center gap-1.5 rounded-full border border-[var(--color-ink-100)] bg-white/70 px-3 py-1.5 text-[11.5px] font-medium text-[var(--color-ink-600)] hover:bg-white cursor-pointer dark:bg-white/[0.05] dark:hover:bg-white/[0.12]"
            >
              <RefreshCw className={cn("h-3 w-3", loading && "animate-spin")} strokeWidth={2.4} />
              Refresh
            </button>
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
            <BarChart data={chartData} margin={{ top: 10, right: 10, left: 0, bottom: 10 }}>
              <CartesianGrid strokeDasharray="3 3" stroke="var(--color-ink-100)" />
              <XAxis dataKey="operator" tick={{ fontSize: 10 }} height={30} interval={0} />
              <YAxis tick={{ fontSize: 11 }} allowDecimals={false} />
              <Tooltip />
              <Legend wrapperStyle={{ fontSize: 11 }} />
              <Bar dataKey="completed" stackId="a" name="Completed" fill="var(--color-mint, #4ade80)" />
              <Bar dataKey="failed" stackId="a" name="Failed" fill="var(--color-coral, #f87171)" />
              <Bar dataKey="rejected" stackId="a" name="Rejected" fill="var(--color-wine, #991b1b)" />
              <Bar dataKey="cancelled" stackId="a" name="Cancelled" fill="var(--color-ink-400, #94a3b8)" />
              <Bar dataKey="inProgress" stackId="a" name="In progress" fill="var(--color-amber, #f59e0b)" />
            </BarChart>
          </ResponsiveContainer>
          </ChartMount>
        </div>

        <div className="mt-4 overflow-x-auto">
          <table className="w-full text-left">
            <DataTableHead>
              <TableTh density="compact">Operator</TableTh>
              <TableTh density="compact" align="right">Trips</TableTh>
              <TableTh density="compact" align="right">Completed</TableTh>
              <TableTh density="compact" align="right">Failed</TableTh>
              <TableTh density="compact" align="right">Rejected</TableTh>
              <TableTh density="compact" align="right">Cancelled</TableTh>
              <TableTh density="compact" align="right">In progress</TableTh>
              <TableTh density="compact" align="right">Success</TableTh>
              <TableTh density="compact" align="right">Median</TableTh>
              <TableTh density="compact" align="right">P95</TableTh>
              <TableTh density="compact" align="right">SLA breach</TableTh>
            </DataTableHead>
            <DataTableBody>
              {rows.length === 0 && loading && <TableSkeletonRows colSpan={11} rows={3} />}
              {rows.length === 0 && !loading && (
                <tr>
                  <TableTd
                    density="compact"
                    colSpan={11}
                    className="py-6 text-center text-[var(--color-ink-400)]"
                  >
                    No manual trips in this window.
                  </TableTd>
                </tr>
              )}
              {rows.map((r, i) => (
                <DataRow key={r.operator} delayIndex={i}>
                  <TableTd density="compact" className="font-semibold">
                    {r.operator}
                  </TableTd>
                  <Num value={r.totalTrips} />
                  <Num value={r.completed} />
                  <Num value={r.failed} />
                  <Num value={r.rejected} />
                  <Num value={r.cancelled} />
                  <Num value={r.inProgress} />
                  <TableTd
                    density="compact"
                    align="right"
                    className={cn(
                      "font-mono tabular-nums",
                      r.successRate >= 0.95
                        ? "text-[var(--color-success)]"
                        : r.successRate >= 0.8
                          ? "text-[var(--color-amber-600)]"
                          : "text-[var(--color-coral)]",
                    )}
                  >
                    {(r.successRate * 100).toFixed(1)}%
                  </TableTd>
                  <TableTd density="compact" align="right" className="font-mono tabular-nums">
                    {fmtDuration(r.medianTimeToCompleteSec)}
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

function Num({ value }: { value: number }) {
  return (
    <TableTd density="compact" align="right" className="font-mono tabular-nums">
      {value.toLocaleString("en-US")}
    </TableTd>
  );
}

function Tile({ label, value }: { label: string; value: string }) {
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
