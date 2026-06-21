"use client";

import { Activity, CircleCheck, AlertTriangle, AlertCircle, HelpCircle, RefreshCw } from "lucide-react";
import { motion } from "motion/react";
import { memo, useCallback, useMemo } from "react";
import { GlassCard } from "@/components/primitives/glass-card";
import { StatusPulse } from "@/components/primitives/status-pulse";
import {
  displayName,
  getVendorHealth,
  INFRA_PREFIX,
  type VendorHealthSnapshot,
  type VendorHealthStatus,
} from "@/lib/api/admin-system-status";
import { useDashboardSubscription } from "@/lib/realtime/hubs/dashboard-hub";
import { useProjectionPoll } from "@/lib/hooks/use-projection-poll";
import {
  useVendorIds,
  useVendorSnapshot,
  useVendorSummary,
} from "@/lib/realtime/use-vendor-health";
import { vendorHealthStore } from "@/lib/realtime/vendor-health-store";
import { cn } from "@/lib/utils";
import { DateTime } from "@/components/primitives/date-time";

// Entry animation matches the dashboard cards (kpi-rail / dispatch-funnel
// pattern) — fade + 18px slide-up, 0.55s, cubic-out ease, staggered per
// card index. `initial` fires once on mount so SignalR-driven re-renders
// don't replay the animation.
const ENTRY_EASE = [0.22, 1, 0.36, 1] as const;
const ENTRY_DURATION = 0.55;

// System status page wired through an external store (vendorHealthStore).
// SignalR pushes and the 30s REST poll both write straight into the
// store; React components subscribe to whichever slice they need via
// useSyncExternalStore.
//
// End-to-end per-vendor independence:
//   • Backend fires StatusChanged for one vendor at a time
//   • Broadcaster pushes one SignalR message per event
//   • Frontend store notifies only that vendor's subscriber
//   • Only that ComponentCard re-renders — parent + sibling cards
//     stay mounted untouched
//
// Parent re-renders only when the vendor LIST changes (e.g. OMS is
// added) or when summary tile counts move. Card-level transitions
// never reach the parent.

export function AdminSystemStatusExperience() {
  // REST poll → bulk-write into store. Returned data is ignored — the
  // store is the source of truth for what cards render.
  const fetcher = useCallback(async (signal: AbortSignal) => {
    const data = await getVendorHealth(signal);
    vendorHealthStore.bulkSet(data.vendors);
    return data;
  }, []);
  const { loading, error, refresh, lastUpdated } = useProjectionPoll(fetcher, {
    intervalMs: 30_000,
  });

  // SignalR push → write straight into store. No React state.
  const handleVendorChanged = useCallback((args: unknown[]) => {
    const snapshot = args[0] as VendorHealthSnapshot | undefined;
    if (snapshot?.vendor) vendorHealthStore.setSnapshot(snapshot);
  }, []);
  const { connected: liveConnected } = useDashboardSubscription("vendor-health", {
    VendorHealthChanged: handleVendorChanged,
  } as Record<string, (...args: unknown[]) => void>);

  // Parent subscribes only to: the list shape + summary numbers.
  // Status transitions on individual vendors never re-render this.
  const ids = useVendorIds();
  const infraIds = useMemo(() => ids.filter((id) => id.startsWith(INFRA_PREFIX)), [ids]);
  const vendorIds = useMemo(() => ids.filter((id) => !id.startsWith(INFRA_PREFIX)), [ids]);

  return (
    <div className="space-y-6">
      <motion.header
        className="flex flex-col gap-2 sm:flex-row sm:items-end sm:justify-between"
        initial={{ opacity: 0, y: 18 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ duration: ENTRY_DURATION, ease: ENTRY_EASE }}
      >
        <div>
          <h1 className="text-[1.7rem] font-semibold text-[var(--color-ink-900)]">
            System status
          </h1>
          <p className="mt-1 text-[13px] text-[var(--color-ink-500)]">
            Realtime health of DTMS infrastructure and external vendors. Backend
            polls each component on its own cadence; state machine debounces
            flap; transitions arrive over SignalR.
          </p>
        </div>
        <div className="flex items-center gap-2">
          <LiveBadge connected={liveConnected} />
          <button
            type="button"
            onClick={refresh}
            className="inline-flex items-center gap-1.5 rounded-full border border-[var(--color-ink-100)] bg-white/70 px-3 py-1.5 text-[11.5px] font-medium text-[var(--color-ink-600)] hover:bg-white dark:bg-white/[0.05] dark:hover:bg-white/[0.12]"
          >
            <RefreshCw className={cn("h-3 w-3", loading && "animate-spin")} strokeWidth={2.4} />
            Refresh
          </button>
        </div>
      </motion.header>

      <SummarySection />

      {error && (
        <div className="rounded-xl bg-[var(--color-coral-soft)] px-3 py-2 text-[11.5px] font-medium text-[var(--color-coral)]">
          {error}
        </div>
      )}

      <section>
        <SectionHeader title="Infrastructure" subtitle={`${infraIds.length} component(s) · DTMS dependencies`} />
        <div className="mt-3 grid grid-cols-1 lg:grid-cols-2 gap-4">
          {infraIds.length === 0 && !loading && !error && (
            <GlassCard variant="default" className="p-6 col-span-full text-center text-[12px] text-[var(--color-ink-400)]">
              No infrastructure snapshots yet — backend poller may still be initializing.
            </GlassCard>
          )}
          {infraIds.map((id, i) => (
            <ComponentCard key={id} vendor={id} animationDelay={0.4 + i * 0.06} />
          ))}
        </div>
      </section>

      <section>
        <SectionHeader
          title="External vendors"
          subtitle={`${vendorIds.length} vendor(s) · 3rd-party services`}
        />
        <div className="mt-3 grid grid-cols-1 lg:grid-cols-2 gap-4">
          {vendorIds.length === 0 && !loading && !error && (
            <GlassCard variant="default" className="p-6 col-span-full text-center text-[12px] text-[var(--color-ink-400)]">
              No vendor snapshots yet.
            </GlassCard>
          )}
          {vendorIds.map((id, i) => (
            // Stagger after infra finishes so the eye lands top-to-bottom.
            <ComponentCard
              key={id}
              vendor={id}
              animationDelay={0.4 + (infraIds.length + i) * 0.06}
            />
          ))}
        </div>
      </section>

      {lastUpdated && (
        <p className="text-[11px] text-[var(--color-ink-400)]">
          REST snapshot last refreshed at{" "}
          <DateTime value={lastUpdated} variant="time" /> · auto-poll every 30s.
        </p>
      )}
    </div>
  );
}

// ────────────────────────────────────────────────────────────────────

function LiveBadge({ connected }: { connected: boolean }) {
  return (
    <span
      className={cn(
        "inline-flex items-center gap-1.5 rounded-full px-2.5 py-1 text-[11px] font-medium",
        connected
          ? "bg-[var(--color-success)]/10 text-[var(--color-success)]"
          : "bg-[var(--color-ink-100)] text-[var(--color-ink-500)]",
      )}
      title={connected ? "Live updates via SignalR" : "Reconnecting…"}
    >
      <StatusPulse tone={connected ? "success" : "amber"} size="sm" />
      {connected ? "Live" : "Polling"}
    </span>
  );
}

// ────────────────────────────────────────────────────────────────────

// Summary is its own component so summary-tile re-renders never reach
// the parent's card lists.
function SummarySection() {
  const summary = useVendorSummary();
  return (
    <section className="grid grid-cols-2 md:grid-cols-4 gap-3">
      <SummaryTile index={0} icon={<Activity className="h-4 w-4" />} label="Components" value={summary.total} />
      <SummaryTile
        index={1}
        icon={<CircleCheck className="h-4 w-4" />}
        label="Healthy"
        value={summary.healthy}
        tone="mint"
      />
      <SummaryTile
        index={2}
        icon={<AlertTriangle className="h-4 w-4" />}
        label="Degraded"
        value={summary.degraded}
        tone="amber"
      />
      <SummaryTile
        index={3}
        icon={<AlertCircle className="h-4 w-4" />}
        label="Unhealthy"
        value={summary.unhealthy}
        tone="coral"
      />
    </section>
  );
}

function SectionHeader({ title, subtitle }: { title: string; subtitle: string }) {
  return (
    <div className="flex items-baseline justify-between gap-3">
      <h2 className="text-[14px] font-semibold uppercase tracking-wider text-[var(--color-ink-700)] dark:text-[var(--color-ink-200)]">
        {title}
      </h2>
      <span className="text-[11px] text-[var(--color-ink-400)]">{subtitle}</span>
    </div>
  );
}

// ────────────────────────────────────────────────────────────────────

type SummaryTileProps = {
  icon: React.ReactNode;
  label: string;
  value: number;
  tone?: "mint" | "amber" | "coral" | "ink";
  index?: number;
};

const SummaryTile = memo(function SummaryTile({ icon, label, value, tone, index = 0 }: SummaryTileProps) {
  const variant =
    tone === "mint" ? "pastel-mint"
    : tone === "amber" ? "pastel-peach"
    : tone === "coral" ? "pastel-lavender"
    : "default";
  return (
    <GlassCard
      variant={variant as "default" | "pastel-mint" | "pastel-peach" | "pastel-lavender"}
      className="p-4"
      initial={{ opacity: 0, y: 18 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: ENTRY_DURATION, delay: 0.15 + index * 0.06, ease: ENTRY_EASE }}
    >
      <div className="flex items-center gap-2 text-[11px] font-medium uppercase tracking-wider text-[var(--color-ink-500)]">
        {icon}
        {label}
      </div>
      <div className="mt-1 text-[1.6rem] font-semibold text-[var(--color-ink-900)]">
        {value.toLocaleString()}
      </div>
    </GlassCard>
  );
});

// ────────────────────────────────────────────────────────────────────

// Each card subscribes to its own vendor slice via useSyncExternalStore.
// When postgres transitions, only this component (for vendor="infra:postgres")
// re-renders — parent, sibling cards, summary tile (if numbers unchanged)
// all stay put. animationDelay is only read on mount (motion's `initial`),
// so SignalR-driven updates never replay the entrance animation.
function ComponentCard({ vendor, animationDelay = 0 }: { vendor: string; animationDelay?: number }) {
  const snapshot = useVendorSnapshot(vendor);
  if (!snapshot) return null;

  const meta = statusMeta(snapshot.status);

  return (
    <GlassCard
      variant="default"
      className="p-5"
      initial={{ opacity: 0, y: 18 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: ENTRY_DURATION, delay: animationDelay, ease: ENTRY_EASE }}
    >
      <div className="flex items-start justify-between gap-3">
        <div>
          <div className="flex items-center gap-2">
            <StatusPulse tone={meta.tone} size="md" />
            <span className="text-[15px] font-semibold uppercase tracking-wider text-[var(--color-ink-900)]">
              {displayName(snapshot)}
            </span>
          </div>
          <div className={cn("mt-0.5 text-[12.5px] font-medium", meta.textClass)}>
            {snapshot.status}
          </div>
        </div>
        <meta.Icon className={cn("h-5 w-5", meta.textClass)} strokeWidth={2.2} />
      </div>

      <div className="mt-4 grid grid-cols-2 gap-x-4 gap-y-2 text-[12px]">
        <Field
          label="Latency"
          value={snapshot.latencyMs != null ? `${snapshot.latencyMs} ms` : "—"}
        />
        <Field
          label="Streak"
          value={
            snapshot.status === "Healthy"
              ? `✓ ${snapshot.consecutiveSuccesses} consecutive`
              : snapshot.consecutiveFailures > 0
                ? `✗ ${snapshot.consecutiveFailures} consecutive`
                : "—"
          }
        />
        <Field
          label="Last checked"
          value={<DateTime value={snapshot.lastCheckedAt} variant="relative" />}
        />
        <Field
          label="Last changed"
          value={<DateTime value={snapshot.lastChangedAt} variant="relative" />}
        />
        {(snapshot.code || snapshot.message) && (
          <Field
            label="Detail"
            value={
              snapshot.code && snapshot.message
                ? `${snapshot.code} · ${snapshot.message}`
                : snapshot.code ?? snapshot.message ?? "—"
            }
          />
        )}
        {snapshot.failureReason && (
          <div className="col-span-2 mt-1 rounded-lg bg-[var(--color-coral)]/10 px-3 py-2 text-[11.5px] text-[var(--color-coral)]">
            {snapshot.failureReason}
          </div>
        )}
      </div>
    </GlassCard>
  );
}

type FieldProps = { label: string; value: React.ReactNode; title?: string };
function Field({ label, value, title }: FieldProps) {
  return (
    <div>
      <div className="text-[10px] font-medium uppercase tracking-wider text-[var(--color-ink-400)]">
        {label}
      </div>
      <div className="text-[12.5px] text-[var(--color-ink-800)] dark:text-[var(--color-ink-100)]" title={title}>
        {value}
      </div>
    </div>
  );
}

// ────────────────────────────────────────────────────────────────────

type StatusMeta = {
  tone: "success" | "amber" | "coral" | "brand";
  textClass: string;
  Icon: typeof CircleCheck;
};

function statusMeta(status: VendorHealthStatus): StatusMeta {
  switch (status) {
    case "Healthy":
      return { tone: "success", textClass: "text-[var(--color-success)]", Icon: CircleCheck };
    case "Degraded":
      return { tone: "amber", textClass: "text-[var(--color-amber)]", Icon: AlertTriangle };
    case "Unhealthy":
      return { tone: "coral", textClass: "text-[var(--color-coral)]", Icon: AlertCircle };
    default:
      return { tone: "brand", textClass: "text-[var(--color-brand-500)]", Icon: HelpCircle };
  }
}

