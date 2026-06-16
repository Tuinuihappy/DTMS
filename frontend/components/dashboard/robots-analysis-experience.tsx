"use client";

import { Battery, BatteryLow, RefreshCw, Truck, Wrench } from "lucide-react";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { GlassCard } from "@/components/primitives/glass-card";
import { SectionLabel } from "@/components/primitives/section-label";
import { DataFreshnessChip } from "@/components/projection/data-freshness-chip";
import { FleetUtilizationChart } from "./fleet-utilization-chart";
import {
  getFleetUtilization,
  type FleetUtilizationBucket,
} from "@/lib/api/dashboard";
import { useProjectionPoll } from "@/lib/hooks/use-projection-poll";
import { useDashboardSubscription } from "@/lib/realtime/hubs/dashboard-hub";
import { cn } from "@/lib/utils";

// Phase P3.2 — Robot analysis subpage. Reads fleet.FleetUtilizationHourly
// snapshots written by the background snapshot service and renders:
//   1. Current state strip (latest snapshot row, colour-coded)
//   2. Stacked area chart of state distribution over time (24h / 7d / 30d)

type Window = "24h" | "7d" | "30d";

const WINDOW_HOURS: Record<Window, number> = {
  "24h": 24,
  "7d": 24 * 7,
  "30d": 24 * 30,
};

function buildWindow(w: Window) {
  const now = new Date();
  const to = new Date(
    Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate(), now.getUTCHours() + 1),
  );
  const from = new Date(to.getTime() - WINDOW_HOURS[w] * 60 * 60 * 1000);
  return { fromUtc: from.toISOString(), toUtc: to.toISOString() };
}

export function RobotsAnalysisExperience() {
  const [window, setWindow] = useState<Window>("24h");

  const fetcher = useCallback(
    (signal: AbortSignal) => getFleetUtilization(buildWindow(window), signal),
    [window],
  );
  const { data, loading, error, refresh, lastUpdated } = useProjectionPoll(fetcher, {
    intervalMs: 30_000,
  });

  // Phase P3.x — DashboardHub live updates from FleetUtilizationSnapshotService.
  // The hosted service ticks every minute and enqueues a "fleet" board hint
  // after each successful UpsertCurrentBucketAsync; the batcher coalesces
  // hints in its 250 ms window. Debounce locally so we never trigger more
  // than one refetch per second even if the snapshot writer ever fires faster.
  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  useDashboardSubscription("fleet", {
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

  // Current-state strip uses the latest snapshot (independent of window).
  const latest = data?.latest;

  const stateTiles = useMemo(() => {
    const l = latest ?? EMPTY_BUCKET;
    return [
      { label: "Busy", value: l.busy, total: l.total, tone: "amber" as const, icon: <Truck className="h-4 w-4" strokeWidth={2.2} /> },
      { label: "Idle", value: l.idle, total: l.total, tone: "ink" as const, icon: <Truck className="h-4 w-4" strokeWidth={2.2} /> },
      { label: "Charging", value: l.charging, total: l.total, tone: "brand" as const, icon: <Battery className="h-4 w-4" strokeWidth={2.2} /> },
      { label: "Maintenance", value: l.maintenance, total: l.total, tone: "coral" as const, icon: <Wrench className="h-4 w-4" strokeWidth={2.2} /> },
      { label: "Low battery", value: l.lowBattery, total: l.total, tone: "coral" as const, icon: <BatteryLow className="h-4 w-4" strokeWidth={2.2} /> },
    ];
  }, [latest]);

  return (
    <div className="space-y-6">
      <header className="flex flex-col gap-2 sm:flex-row sm:items-end sm:justify-between">
        <div>
          <h1 className="text-[1.7rem] font-semibold text-[var(--color-ink-900)]">
            Robot analysis
          </h1>
          <p className="mt-1 text-[13px] text-[var(--color-ink-500)]">
            Hourly vehicle-state distribution from the fleet utilization projection.
          </p>
        </div>
        <div className="flex items-center gap-1.5">
          {data?.lastEventAt && (
            <DataFreshnessChip lastEventAt={lastUpdated ?? data.lastEventAt} />
          )}
          <WindowToggle value={window} onChange={setWindow} />
          <button
            type="button"
            onClick={refresh}
            className="inline-flex items-center gap-1.5 rounded-full border border-[var(--color-ink-100)] bg-white/70 px-3 py-1.5 text-[11.5px] font-medium text-[var(--color-ink-600)] hover:bg-white cursor-pointer dark:bg-white/[0.05] dark:hover:bg-white/[0.12]"
            aria-label="Refresh"
          >
            <RefreshCw className={cn("h-3 w-3", loading && "animate-spin")} strokeWidth={2.4} />
            Refresh
          </button>
        </div>
      </header>

      {/* Current-state strip */}
      <section className="grid grid-cols-2 md:grid-cols-5 gap-3">
        {stateTiles.map((s, i) => (
          <StateTile key={s.label} {...s} delay={i * 0.05} />
        ))}
      </section>

      {/* Time-series chart */}
      <GlassCard variant="default" className="p-5">
        <SectionLabel
          title="Utilization over time"
          subtitle={`Trailing ${window} · ${data?.buckets.length ?? 0} buckets · Total fleet ${latest?.total ?? 0}`}
        />
        {error && (
          <div className="mt-4 rounded-xl bg-[#fde0db] px-3 py-2 text-[11.5px] font-medium text-[var(--color-coral)] dark:bg-[#3a1a17]">
            {error}
          </div>
        )}
        <div className="mt-4">
          <FleetUtilizationChart buckets={data?.buckets ?? []} />
        </div>
      </GlassCard>
    </div>
  );
}

const EMPTY_BUCKET: FleetUtilizationBucket = {
  bucketHour: "",
  active: 0,
  busy: 0,
  idle: 0,
  charging: 0,
  maintenance: 0,
  lowBattery: 0,
  offline: 0,
  total: 0,
};

function StateTile({
  label,
  value,
  total,
  tone,
  icon,
  delay,
}: {
  label: string;
  value: number;
  total: number;
  tone: "brand" | "amber" | "coral" | "ink";
  icon: React.ReactNode;
  delay: number;
}) {
  const toneClasses: Record<typeof tone, string> = {
    brand: "from-[var(--color-pastel-sky)] to-[#c7d4ff] text-[var(--color-brand-900)] dark:to-[#2a3a7a] dark:text-[var(--color-pastel-sky-ink)]",
    amber: "from-[var(--color-amber-soft)] to-[#fcd398] text-[#8a4a07] dark:to-[#6a4a1c] dark:text-[var(--color-amber)]",
    coral: "from-[#fde0db] to-[#f8b5aa] text-[var(--color-coral)] dark:to-[#5c1f17] dark:text-[var(--color-coral)]",
    ink:   "from-[var(--color-ink-100)] to-[#cfd6e6] text-[var(--color-ink-800)] dark:to-[#3a4870] dark:text-[var(--color-ink-700)]",
  };
  const pct = total > 0 ? Math.round((value / total) * 100) : 0;

  return (
    <GlassCard
      variant="default"
      initial={{ opacity: 0, y: 12 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.4, delay }}
      className="p-4"
    >
      <div className="flex items-start justify-between gap-2">
        <span
          className={cn(
            "grid h-8 w-8 place-items-center rounded-[10px] bg-gradient-to-br shadow-[inset_0_1px_0_rgba(255,255,255,0.8)]",
            toneClasses[tone],
          )}
        >
          {icon}
        </span>
        <span className="text-[10.5px] font-mono tabular-nums text-[var(--color-ink-400)]">
          {pct}%
        </span>
      </div>
      <div className="mt-3 font-mono text-[1.6rem] font-semibold tabular-nums leading-none text-[var(--color-ink-900)]">
        {value.toLocaleString()}
      </div>
      <div className="mt-2 text-[10.5px] uppercase tracking-[0.12em] font-medium text-[var(--color-ink-500)]">
        {label}
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
  const options: Window[] = ["24h", "7d", "30d"];
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
