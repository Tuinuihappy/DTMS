"use client";

import { Calendar, RefreshCw } from "lucide-react";
import { useCallback, useMemo, useState } from "react";
import { GlassCard } from "@/components/primitives/glass-card";
import { SectionLabel } from "@/components/primitives/section-label";
import { DataFreshnessChip } from "@/components/projection/data-freshness-chip";
import { OrderStatusChart } from "./order-status-chart";
import { getOrderFunnel } from "@/lib/api/dashboard";
import { useProjectionPoll } from "@/lib/hooks/use-projection-poll";
import { cn } from "@/lib/utils";

// Phase P3.2 — Order analysis subpage. Drills the OrderFunnelHourly
// projection across a selectable trailing window (24h / 7d / 30d) into
// a stacked-area chart + per-status totals + per-bucket breakdown.

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

export function OrdersAnalysisExperience() {
  const [window, setWindow] = useState<Window>("7d");

  const fetcher = useCallback(
    (signal: AbortSignal) => getOrderFunnel(buildWindow(window), signal),
    [window],
  );
  const { data, loading, error, refresh, lastUpdated } = useProjectionPoll(fetcher, {
    intervalMs: 30_000,
  });

  // Derive a per-status grand-total row from the totals object so the
  // header cards don't re-read the .totals shape in every render.
  const summary = useMemo(() => {
    const t = data?.totals ?? {
      confirmed: 0, dispatched: 0, inProgress: 0,
      completed: 0, partiallyCompleted: 0,
      failed: 0, cancelled: 0, rejected: 0,
      held: 0, released: 0,
    };
    return [
      { label: "Confirmed",   value: t.confirmed,                                          tone: "ink" },
      { label: "Dispatched",  value: t.dispatched + t.inProgress,                          tone: "amber" },
      { label: "Completed",   value: t.completed + t.partiallyCompleted,                   tone: "success" },
      { label: "Lost",        value: t.failed + t.cancelled + t.rejected,                  tone: "coral" },
      { label: "On hold",     value: t.held,                                               tone: "lavender" },
    ];
  }, [data]);

  return (
    <div className="space-y-6">
      <header className="flex flex-col gap-2 sm:flex-row sm:items-end sm:justify-between">
        <div>
          <h1 className="text-[1.7rem] font-semibold text-[var(--color-ink-900)]">
            Order analysis
          </h1>
          <p className="mt-1 text-[13px] text-[var(--color-ink-500)]">
            Stacked hourly counts by order status, drawn from the OrderFunnel projection.
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

      {/* Summary tiles */}
      <section className="grid grid-cols-2 md:grid-cols-5 gap-3">
        {summary.map((s, i) => (
          <GlassCard
            key={s.label}
            variant="default"
            initial={{ opacity: 0, y: 12 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.4, delay: 0.05 + i * 0.05 }}
            className="p-4"
          >
            <div className="text-[10.5px] font-semibold uppercase tracking-[0.12em] text-[var(--color-ink-500)]">
              {s.label}
            </div>
            <div className="mt-2 font-mono text-[1.8rem] font-semibold tabular-nums leading-none text-[var(--color-ink-900)]">
              {s.value.toLocaleString()}
            </div>
          </GlassCard>
        ))}
      </section>

      {/* Stacked area chart */}
      <GlassCard variant="default" className="p-5">
        <SectionLabel
          title="Hourly bucket history"
          subtitle={`Trailing ${window} · ${data?.buckets.length ?? 0} buckets`}
          action={
            <span className="inline-flex items-center gap-1.5 rounded-full border border-[var(--color-ink-100)] bg-white/70 px-3 py-1.5 text-[11.5px] font-medium text-[var(--color-ink-600)] dark:bg-white/[0.05]">
              <Calendar className="h-3 w-3" strokeWidth={2.4} />
              {window}
            </span>
          }
        />

        {error && (
          <div className="mt-4 rounded-xl bg-[#fde0db] px-3 py-2 text-[11.5px] font-medium text-[var(--color-coral)] dark:bg-[#3a1a17]">
            {error}
          </div>
        )}

        <div className="mt-4">
          <OrderStatusChart buckets={data?.buckets ?? []} />
        </div>
      </GlassCard>
    </div>
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
