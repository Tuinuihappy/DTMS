"use client";

import { Activity, AlertTriangle, CheckCircle2, ListChecks } from "lucide-react";
import { useCallback, useEffect, useRef } from "react";
import { GlassCard } from "@/components/primitives/glass-card";
import { NumberTicker } from "@/components/primitives/number-ticker";
import { StatusPulse } from "@/components/primitives/status-pulse";
import { DataFreshnessChip } from "@/components/projection/data-freshness-chip";
import { getOrderFunnel, type OrderFunnelTotals } from "@/lib/api/dashboard";
import { useProjectionPoll } from "@/lib/hooks/use-projection-poll";
import { useDashboardSubscription } from "@/lib/realtime/hubs/dashboard-hub";
import { cn } from "@/lib/utils";

// Phase P3 — Real data from deliveryorder.OrderFunnelHourly. Each tile
// pulls a slice of the totals returned for the trailing 24 hours.

type KpiTone = "brand" | "amber" | "success" | "coral";

type KpiSpec = {
  icon: React.ReactNode;
  label: string;
  value: number;
  pulse?: boolean;
  tone: KpiTone;
};

const toneRing: Record<KpiTone, string> = {
  brand:
    "from-[var(--color-pastel-sky)] to-[#c7d4ff] text-[var(--color-brand-900)] dark:to-[#2a3a7a] dark:text-[var(--color-pastel-sky-ink)]",
  amber:
    "from-[var(--color-amber-soft)] to-[#fcd398] text-[#8a4a07] dark:to-[#6a4a1c] dark:text-[var(--color-amber)]",
  success:
    "from-[var(--color-success-soft)] to-[#b6e8cf] text-[var(--color-success)] dark:to-[#1f5a40] dark:text-[var(--color-success)]",
  coral:
    "from-[#fde0db] to-[#f8b5aa] text-[var(--color-coral)] dark:to-[#5c1f17] dark:text-[var(--color-coral)]",
};

function defaultWindow() {
  const now = new Date();
  const to = new Date(
    Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate(), now.getUTCHours() + 1),
  );
  const from = new Date(to.getTime() - 24 * 60 * 60 * 1000);
  return { fromUtc: from.toISOString(), toUtc: to.toISOString() };
}

export function KpiRail() {
  const fetcher = useCallback(
    (signal: AbortSignal) => getOrderFunnel(defaultWindow(), signal),
    [],
  );
  const { data, lastUpdated, refresh } = useProjectionPoll(fetcher, { intervalMs: 15_000 });

  // Phase P3.x — KPI rail reads the same OrderFunnel projection as
  // /dashboard/orders, so it subscribes to the same "orders" board. The
  // OrderFunnelProjector enqueues a hint per processed event; the batcher
  // coalesces hints in its 250 ms window. Debounce locally so a burst
  // (e.g. cancel-storm) doesn't trigger N refetches in close succession.
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

  const totals = data?.totals ?? EMPTY_TOTALS;
  const kpis = buildKpis(totals);

  return (
    <div className="col-span-12 md:col-span-6 lg:col-span-4 grid grid-cols-2 lg:grid-cols-1 gap-4">
      {kpis.map((k, i) => (
        <GlassCard
          key={k.label}
          variant="default"
          interactive
          initial={{ opacity: 0, y: 18 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.55, delay: 0.2 + i * 0.06, ease: [0.22, 1, 0.36, 1] }}
          className="p-5"
        >
          <div className="flex items-start justify-between gap-3">
            <span
              className={cn(
                "grid h-9 w-9 place-items-center rounded-[12px] bg-gradient-to-br shadow-[inset_0_1px_0_rgba(255,255,255,0.8)]",
                toneRing[k.tone],
              )}
            >
              {k.icon}
            </span>
            {k.pulse ? (
              <span className="inline-flex items-center gap-1.5 text-[10.5px] font-semibold uppercase tracking-[0.12em] text-[var(--color-amber)]">
                <StatusPulse tone="amber" />
                Live
              </span>
            ) : i === 0 && data?.lastEventAt ? (
              <DataFreshnessChip lastEventAt={lastUpdated ?? data.lastEventAt} />
            ) : null}
          </div>

          <div className="mt-5">
            <div className="font-mono text-[2rem] font-semibold leading-none tabular-nums text-[var(--color-ink-900)]">
              <NumberTicker value={k.value} decimals={0} />
            </div>
            <div className="mt-2 text-[11.5px] uppercase tracking-[0.12em] font-medium text-[var(--color-ink-500)]">
              {k.label}
            </div>
          </div>
        </GlassCard>
      ))}
    </div>
  );
}

const EMPTY_TOTALS: OrderFunnelTotals = {
  confirmed: 0, dispatched: 0, inProgress: 0,
  completed: 0, partiallyCompleted: 0,
  failed: 0, cancelled: 0, rejected: 0,
  held: 0, released: 0,
};

function buildKpis(t: OrderFunnelTotals): KpiSpec[] {
  const confirmedTotal = t.confirmed;
  const inFlight = t.dispatched + t.inProgress;
  const completed = t.completed + t.partiallyCompleted;
  const lost = t.failed + t.cancelled + t.rejected;

  return [
    {
      icon: <ListChecks className="h-4 w-4" strokeWidth={2.2} />,
      label: "Confirmed (24h)",
      value: confirmedTotal,
      tone: "brand",
    },
    {
      icon: <Activity className="h-4 w-4" strokeWidth={2.2} />,
      label: "In flight",
      value: inFlight,
      pulse: true,
      tone: "amber",
    },
    {
      icon: <CheckCircle2 className="h-4 w-4" strokeWidth={2.2} />,
      label: "Completed (24h)",
      value: completed,
      tone: "success",
    },
    {
      icon: <AlertTriangle className="h-4 w-4" strokeWidth={2.2} />,
      label: "Lost (24h)",
      value: lost,
      tone: "coral",
    },
  ];
}
