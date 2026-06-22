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
  getSlaBreachReport,
  ordersReportCsvUrl,
  type Window,
} from "@/lib/api/reports";
import { useProjectionPoll } from "@/lib/hooks/use-projection-poll";
import { cn } from "@/lib/utils";
import {
  DataRow,
  DataTableBody,
  DataTableHead,
  TableTd,
  TableTh,
} from "@/components/primitives/data-table";

export function SlaBreachReport({ window }: { window: Window }) {
  const fetcher = useCallback(
    (signal: AbortSignal) => getSlaBreachReport(window, signal),
    [window],
  );
  const { data, loading, error, refresh } = useProjectionPoll(fetcher, {
    intervalMs: 5 * 60_000,
  });

  const csvHref = ordersReportCsvUrl(window);
  const chartData = (data?.rows ?? []).map((r) => ({
    priority: r.priority,
    confirmBreached: r.confirmBreached,
    completeBreached: r.completeBreached,
    onTime: Math.max(0, r.totalOrders - Math.max(r.confirmBreached, r.completeBreached)),
  }));

  const overallConfirmRate =
    data && data.totalOrders > 0 ? (data.totalConfirmBreached / data.totalOrders) * 100 : 0;
  const overallCompleteRate =
    data && data.totalOrders > 0 ? (data.totalCompleteBreached / data.totalOrders) * 100 : 0;

  return (
    <div className="space-y-4">
      <section className="grid grid-cols-2 md:grid-cols-4 gap-3">
        <Tile label="Total orders" value={(data?.totalOrders ?? 0).toLocaleString()} />
        <Tile
          label="Confirm SLA breach"
          value={`${data?.totalConfirmBreached ?? 0} (${overallConfirmRate.toFixed(1)}%)`}
          tone="amber"
        />
        <Tile
          label="Complete SLA breach"
          value={`${data?.totalCompleteBreached ?? 0} (${overallCompleteRate.toFixed(1)}%)`}
          tone="coral"
        />
        <Tile
          label="Thresholds"
          value="4h / 24h"
          hint="Confirm < 4h · Complete < 24h"
        />
      </section>

      <GlassCard variant="default" className="p-5">
        <SectionLabel
          title="SLA breach by priority"
          subtitle="Confirm breach = TimeToConfirm > 4h · Complete breach = TimeToComplete > 24h."
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
              <XAxis dataKey="priority" tick={{ fontSize: 11 }} />
              <YAxis tick={{ fontSize: 11 }} allowDecimals={false} />
              <Tooltip />
              <Legend wrapperStyle={{ fontSize: 11 }} />
              <Bar dataKey="onTime" stackId="a" name="On-time" fill="var(--color-mint, #4ade80)" />
              <Bar
                dataKey="confirmBreached"
                stackId="a"
                name="Confirm breach"
                fill="var(--color-amber-500, #f59e0b)"
              />
              <Bar
                dataKey="completeBreached"
                stackId="a"
                name="Complete breach"
                fill="var(--color-coral, #f87171)"
              />
            </BarChart>
          </ResponsiveContainer>
          </ChartMount>
        </div>

        <div className="mt-4 overflow-x-auto">
          <table className="w-full text-left">
            <DataTableHead>
              <TableTh density="compact">Priority</TableTh>
              <TableTh density="compact" align="right">Total</TableTh>
              <TableTh density="compact" align="right">Confirm breach</TableTh>
              <TableTh density="compact" align="right">Complete breach</TableTh>
              <TableTh density="compact" align="right">Confirm rate</TableTh>
              <TableTh density="compact" align="right">Complete rate</TableTh>
            </DataTableHead>
            <DataTableBody>
              {(data?.rows ?? []).length === 0 && (
                <tr>
                  <TableTd
                    density="compact"
                    colSpan={6}
                    className="py-6 text-center text-[var(--color-ink-400)]"
                  >
                    {loading ? "Loading…" : "No orders in this window."}
                  </TableTd>
                </tr>
              )}
              {(data?.rows ?? []).map((r, i) => (
                <DataRow key={r.priority} delayIndex={i}>
                  <TableTd density="compact" className="font-semibold">
                    {r.priority}
                  </TableTd>
                  <TableTd density="compact" align="right" className="font-mono tabular-nums">
                    {r.totalOrders.toLocaleString()}
                  </TableTd>
                  <TableTd density="compact" align="right" className="font-mono tabular-nums">
                    {r.confirmBreached.toLocaleString()}
                  </TableTd>
                  <TableTd density="compact" align="right" className="font-mono tabular-nums">
                    {r.completeBreached.toLocaleString()}
                  </TableTd>
                  <TableTd
                    density="compact"
                    align="right"
                    className="font-mono tabular-nums text-[var(--color-amber-600)]"
                  >
                    {(r.confirmBreachRate * 100).toFixed(1)}%
                  </TableTd>
                  <TableTd
                    density="compact"
                    align="right"
                    className="font-mono tabular-nums text-[var(--color-coral)]"
                  >
                    {(r.completeBreachRate * 100).toFixed(1)}%
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
      {hint && <div className="mt-1 text-[10.5px] text-[var(--color-ink-400)]">{hint}</div>}
    </GlassCard>
  );
}
