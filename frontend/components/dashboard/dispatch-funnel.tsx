"use client";

import { Calendar, Filter, RefreshCw } from "lucide-react";
import { motion } from "motion/react";
import { useCallback, useEffect, useRef } from "react";
import { GlassCard } from "@/components/primitives/glass-card";
import { SectionLabel } from "@/components/primitives/section-label";
import { DataFreshnessChip } from "@/components/projection/data-freshness-chip";
import { getOrderFunnel } from "@/lib/api/dashboard";
import { useProjectionPoll } from "@/lib/hooks/use-projection-poll";
import { useDashboardSubscription } from "@/lib/realtime/hubs/dashboard-hub";
import { cn } from "@/lib/utils";

// Phase P3 — Real data from deliveryorder.OrderFunnelHourly via the
// /api/dashboard/order-funnel endpoint. Replaces the previous mock
// data. The 5 funnel stages map to projection columns:
//   Confirmed → Dispatched → InProgress → Completed
// with a 5th stage tracking terminal failures (Failed + Cancelled +
// Rejected) so operators can spot pipeline leakage at a glance.

type FunnelTone = "ink" | "amber" | "coral" | "success";

type FunnelStage = {
  label: string;
  count: number;
  tone: FunnelTone;
};

const toneClass: Record<FunnelTone, string> = {
  ink: "bg-[var(--color-ink-800)]",
  amber: "bg-[var(--color-amber)]",
  coral: "bg-[var(--color-coral)]",
  success: "bg-[var(--color-success)]",
};
const toneText: Record<FunnelTone, string> = {
  ink: "text-[var(--color-ink-800)]",
  amber: "text-[var(--color-amber)]",
  coral: "text-[var(--color-coral)]",
  success: "text-[var(--color-success)]",
};

// Default window: trailing 24 hours, end-exclusive on the current hour.
function defaultWindow() {
  const now = new Date();
  const to = new Date(
    Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate(), now.getUTCHours() + 1),
  );
  const from = new Date(to.getTime() - 24 * 60 * 60 * 1000);
  return { fromUtc: from.toISOString(), toUtc: to.toISOString() };
}

export function DispatchFunnel() {
  // Pin the window across renders so the poll doesn't churn — wrap in
  // useCallback so the fetcher itself is stable.
  const fetcher = useCallback(
    (signal: AbortSignal) => getOrderFunnel(defaultWindow(), signal),
    [],
  );
  const { data, loading, error, refresh, lastUpdated } = useProjectionPoll(fetcher, {
    intervalMs: 15_000,
  });

  // Phase P3 quick-win — funnel shares the OrderFunnel projection with
  // KpiRail + /dashboard/orders, so subscribe to the same "orders" board.
  // Debounce-refetch keeps a burst (cancel-storm during ops cutover) from
  // hammering the REST endpoint N times within the batcher's 250 ms window.
  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  useDashboardSubscription("orders", {
    CountersUpdated: () => {
      if (debounceRef.current) clearTimeout(debounceRef.current);
      debounceRef.current = setTimeout(() => {
        void refresh();
      }, 500);
    },
  });
  useEffect(() => {
    return () => {
      if (debounceRef.current) clearTimeout(debounceRef.current);
    };
  }, []);

  const stages: FunnelStage[] = data
    ? buildStages(data.totals)
    : EMPTY_STAGES;
  const max = Math.max(1, ...stages.map((f) => f.count));
  const conversion = stages[0].count > 0
    ? ((stages[3].count / stages[0].count) * 100).toFixed(1) + "%"
    : "—";
  const lostCount = stages[4].count;

  return (
    <GlassCard
      variant="default"
      className="col-span-12 md:col-span-6 lg:col-span-5 p-6"
      initial={{ opacity: 0, y: 18 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.55, delay: 0.7, ease: [0.22, 1, 0.36, 1] }}
    >
      <SectionLabel
        title="Dispatch funnel"
        subtitle="Pipeline conversion — last 24 hours"
        action={
          <div className="flex items-center gap-1.5">
            {data?.lastEventAt && <DataFreshnessChip lastEventAt={lastUpdated ?? data.lastEventAt} />}
            <button
              type="button"
              onClick={refresh}
              className="inline-flex items-center gap-1.5 rounded-full border border-[var(--color-ink-100)] bg-white/70 px-3 py-1.5 text-[11.5px] font-medium text-[var(--color-ink-600)] hover:bg-white cursor-pointer dark:bg-white/[0.05] dark:hover:bg-white/[0.12]"
              aria-label="Refresh"
            >
              <RefreshCw className={cn("h-3 w-3", loading && "animate-spin")} strokeWidth={2.4} />
              Refresh
            </button>
            <button
              className="inline-flex items-center gap-1.5 rounded-full border border-[var(--color-ink-100)] bg-white/70 px-3 py-1.5 text-[11.5px] font-medium text-[var(--color-ink-600)] hover:bg-white cursor-pointer dark:bg-white/[0.05] dark:hover:bg-white/[0.12]"
              type="button"
            >
              <Filter className="h-3 w-3" strokeWidth={2.4} />
              All routes
            </button>
            <span className="inline-flex items-center gap-1.5 rounded-full border border-[var(--color-ink-100)] bg-white/70 px-3 py-1.5 text-[11.5px] font-medium text-[var(--color-ink-600)] dark:bg-white/[0.05]">
              <Calendar className="h-3 w-3" strokeWidth={2.4} />
              24h
            </span>
          </div>
        }
      />

      {error && (
        <div className="mt-4 rounded-xl bg-[var(--color-coral-soft)] px-3 py-2 text-[11.5px] font-medium text-[var(--color-coral)]">
          Couldn&apos;t load funnel: {error}
        </div>
      )}

      {/* Bar chart */}
      <div className="mt-8 grid grid-cols-5 gap-4 md:gap-6 h-[200px] items-end">
        {stages.map((f, i) => {
          const ratio = f.count / max;
          const ticks = Math.max(1, Math.round(ratio * 26));
          return (
            <div key={f.label} className="flex flex-col items-center justify-end gap-3">
              <div className="relative w-full h-full flex flex-col justify-end items-center">
                <div className="flex flex-col items-center gap-[3px] w-full">
                  {Array.from({ length: ticks }).map((_, t) => (
                    <motion.span
                      key={t}
                      initial={{ scaleX: 0, opacity: 0 }}
                      animate={{ scaleX: 1, opacity: 1 }}
                      transition={{
                        duration: 0.3,
                        delay: 0.1 + i * 0.05 + t * 0.012,
                        ease: [0.22, 1, 0.36, 1],
                      }}
                      className={cn(
                        "h-[3px] w-full rounded-full origin-bottom",
                        toneClass[f.tone],
                      )}
                      style={{
                        opacity: 0.25 + (t / ticks) * 0.75,
                      }}
                    />
                  ))}
                </div>
              </div>

              <div className="text-center pt-2 border-t border-[var(--color-ink-100)] w-full">
                <div className={cn("font-mono text-[1.15rem] font-semibold leading-none tabular-nums", toneText[f.tone])}>
                  {f.count.toLocaleString("en-US")}
                </div>
                <div className="mt-1.5 text-[10.5px] uppercase tracking-[0.1em] font-medium text-[var(--color-ink-500)]">
                  {f.label}
                </div>
              </div>
            </div>
          );
        })}
      </div>

      <div className="mt-6 inset-divider" />
      <div className="mt-4 flex items-center justify-between text-[11.5px] text-[var(--color-ink-500)]">
        <span>
          Confirmed → Completed:{" "}
          <span className="font-mono font-semibold text-[var(--color-ink-900)]">{conversion}</span>
        </span>
        <span>
          Lost (Failed/Cancelled/Rejected):{" "}
          <span className="font-mono font-semibold text-[var(--color-coral)]">{lostCount.toLocaleString("en-US")}</span>
        </span>
      </div>
    </GlassCard>
  );
}

const EMPTY_STAGES: FunnelStage[] = [
  { label: "Confirmed",  count: 0, tone: "ink" },
  { label: "Dispatched", count: 0, tone: "amber" },
  { label: "In progress", count: 0, tone: "amber" },
  { label: "Completed",  count: 0, tone: "success" },
  { label: "Lost",       count: 0, tone: "coral" },
];

function buildStages(t: {
  confirmed: number; dispatched: number; inProgress: number;
  completed: number; partiallyCompleted: number;
  failed: number; cancelled: number; rejected: number;
}): FunnelStage[] {
  return [
    { label: "Confirmed",   count: t.confirmed,                                                  tone: "ink" },
    { label: "Dispatched",  count: t.dispatched,                                                 tone: "amber" },
    { label: "In progress", count: t.inProgress,                                                 tone: "amber" },
    { label: "Completed",   count: t.completed + t.partiallyCompleted,                           tone: "success" },
    { label: "Lost",        count: t.failed + t.cancelled + t.rejected,                          tone: "coral" },
  ];
}
