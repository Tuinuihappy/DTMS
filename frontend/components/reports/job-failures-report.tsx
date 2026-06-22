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
  getJobFailuresReport,
  jobsExportCsvUrl,
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

/// Phase #9 — Job failures broken down by structured JobFailureCategory
/// (b13 enum). Different angle from the "Top failures" report — that
/// one groups Order.FailureReason text (ops view of *why* orders fail);
/// this one groups Job.FailureCategory enum (engineering view of *what
/// technical class* the failure belongs to).
export function JobFailuresReport({ window }: { window: Window }) {
  const fetcher = useCallback(
    (signal: AbortSignal) => getJobFailuresReport(window, signal),
    [window],
  );
  const { data, loading, error, refresh } = useProjectionPoll(fetcher, {
    intervalMs: 5 * 60_000,
  });

  const csvHref = jobsExportCsvUrl(window);
  const chartData = (data?.categoryTotals ?? []).map((c) => ({
    category: c.category,
    count: c.count,
  }));
  const topCategory = data?.categoryTotals[0];

  return (
    <div className="space-y-4">
      <section className="grid grid-cols-2 md:grid-cols-3 gap-3">
        <Tile label="Job failures" value={(data?.totalFailures ?? 0).toLocaleString("en-US")} tone="coral" />
        <Tile label="Categories" value={(data?.categoryTotals.length ?? 0).toLocaleString("en-US")} />
        <Tile
          label="Top category"
          value={topCategory ? `${topCategory.category}` : "—"}
          hint={topCategory ? `${topCategory.count} (${(topCategory.pct * 100).toFixed(0)}%)` : undefined}
        />
      </section>

      <GlassCard variant="default" className="p-5">
        <SectionLabel
          title="Failures by category"
          subtitle="Structured classification from b13 JobFailureCategory enum. Companion to Top failures (which groups order text)."
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
            <BarChart
              data={chartData}
              layout="vertical"
              margin={{ top: 10, right: 20, left: 0, bottom: 10 }}
            >
              <CartesianGrid strokeDasharray="3 3" stroke="var(--color-ink-100)" />
              <XAxis type="number" tick={{ fontSize: 11 }} allowDecimals={false} />
              <YAxis
                type="category"
                dataKey="category"
                tick={{ fontSize: 11 }}
                width={180}
              />
              <Tooltip />
              <Bar dataKey="count" name="Failures" fill="var(--color-coral, #f87171)" />
            </BarChart>
          </ResponsiveContainer>
          </ChartMount>
        </div>

        <div className="mt-4 overflow-x-auto">
          <table className="w-full text-left">
            <DataTableHead>
              <TableTh density="compact">Category</TableTh>
              <TableTh density="compact">Reason</TableTh>
              <TableTh density="compact" align="right">Count</TableTh>
              <TableTh density="compact" align="right">Retried</TableTh>
              <TableTh density="compact" align="right">% of total</TableTh>
            </DataTableHead>
            <DataTableBody>
              {(data?.rows ?? []).length === 0 && loading && (
                <TableSkeletonRows colSpan={5} rows={3} />
              )}
              {(data?.rows ?? []).length === 0 && !loading && (
                <tr>
                  <TableTd
                    density="compact"
                    colSpan={5}
                    className="py-6 text-center text-[var(--color-ink-400)]"
                  >
                    No job failures in this window.
                  </TableTd>
                </tr>
              )}
              {(data?.rows ?? []).map((r, idx) => (
                <DataRow key={`${r.category}-${r.reason}-${idx}`} delayIndex={idx}>
                  <TableTd
                    density="compact"
                    className="font-mono text-[11.5px] font-semibold text-[var(--color-coral)]"
                  >
                    {r.category}
                  </TableTd>
                  <TableTd density="compact" className="text-[var(--color-ink-700)]">
                    {r.reason}
                  </TableTd>
                  <TableTd density="compact" align="right" className="font-mono tabular-nums">
                    {r.count.toLocaleString("en-US")}
                  </TableTd>
                  <TableTd
                    density="compact"
                    align="right"
                    className="font-mono tabular-nums text-[var(--color-amber-600)]"
                  >
                    {r.retriedCount > 0 ? r.retriedCount.toLocaleString("en-US") : "—"}
                  </TableTd>
                  <TableTd density="compact" align="right" className="font-mono tabular-nums">
                    {(r.pctOfTotal * 100).toFixed(1)}%
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
