"use client";

import { Activity, CircleCheck, AlertTriangle, AlertCircle, HelpCircle, RefreshCw } from "lucide-react";
import { useCallback, useMemo, useState } from "react";
import { GlassCard } from "@/components/primitives/glass-card";
import { StatusPulse } from "@/components/primitives/status-pulse";
import {
  getInfraHealth,
  getVendorHealth,
  type InfraCheck,
  type InfraCheckStatus,
  type VendorHealthSnapshot,
  type VendorHealthStatus,
} from "@/lib/api/admin-system-status";
import { useDashboardSubscription } from "@/lib/realtime/hubs/dashboard-hub";
import { useProjectionPoll } from "@/lib/hooks/use-projection-poll";
import { cn } from "@/lib/utils";

// Vendor health page. Two sections:
//   • Infrastructure — DTMS's own dependencies (postgres / redis / rabbit /
//     masstransit-bus) sourced from /health/ready, polled every 30s. No
//     realtime push — readiness probe is fed from K8s-style polling.
//   • External vendors — RIOT3 (and future OMS) sourced from
//     /api/vendors/health + DashboardHub.VendorHealthChanged. State machine
//     debounces flap so every push is a real transition worth showing.

type AnyStatus = VendorHealthStatus | InfraCheckStatus;

const STATUS_ORDER: Record<AnyStatus, number> = {
  Unhealthy: 0,
  Degraded: 1,
  Unknown: 2,
  Healthy: 3,
};

export function AdminSystemStatusExperience() {
  const vendorFetcher = useCallback(
    (signal: AbortSignal) => getVendorHealth(signal),
    [],
  );
  const infraFetcher = useCallback(
    (signal: AbortSignal) => getInfraHealth(signal),
    [],
  );

  const vendorPoll = useProjectionPoll(vendorFetcher, { intervalMs: 30_000 });
  const infraPoll = useProjectionPoll(infraFetcher, { intervalMs: 30_000 });

  // Apply realtime patches on top of the REST snapshot. Falls back cleanly
  // if the hub never connects: REST polling keeps the state fresh.
  const [patches, setPatches] = useState<Record<string, VendorHealthSnapshot>>({});

  const handleVendorChanged = useCallback((args: unknown[]) => {
    const snapshot = args[0] as VendorHealthSnapshot | undefined;
    if (!snapshot?.vendor) return;
    setPatches((prev) => ({ ...prev, [snapshot.vendor]: snapshot }));
  }, []);

  const { connected: liveConnected } = useDashboardSubscription("vendor-health", {
    VendorHealthChanged: handleVendorChanged,
  } as Record<string, (...args: unknown[]) => void>);

  const vendors: VendorHealthSnapshot[] = useMemo(() => {
    const base = vendorPoll.data?.vendors ?? [];
    const byKey = new Map(base.map((v) => [v.vendor, v]));
    for (const [vendor, patch] of Object.entries(patches)) {
      byKey.set(vendor, patch);
    }
    return [...byKey.values()].sort(
      (a, b) =>
        STATUS_ORDER[a.status] - STATUS_ORDER[b.status] || a.vendor.localeCompare(b.vendor),
    );
  }, [vendorPoll.data, patches]);

  const infraChecks: InfraCheck[] = useMemo(() => {
    const list = infraPoll.data?.checks ?? [];
    return [...list].sort(
      (a, b) =>
        STATUS_ORDER[a.status] - STATUS_ORDER[b.status] || a.name.localeCompare(b.name),
    );
  }, [infraPoll.data]);

  const summary = useMemo(() => {
    const totals = { total: 0, healthy: 0, degraded: 0, unhealthy: 0, unknown: 0 };
    const tally = (status: AnyStatus) => {
      totals.total++;
      if (status === "Healthy") totals.healthy++;
      else if (status === "Degraded") totals.degraded++;
      else if (status === "Unhealthy") totals.unhealthy++;
      else totals.unknown++;
    };
    for (const v of vendors) tally(v.status);
    for (const c of infraChecks) tally(c.status);
    return totals;
  }, [vendors, infraChecks]);

  const loading = vendorPoll.loading || infraPoll.loading;
  const refresh = useCallback(() => {
    vendorPoll.refresh();
    infraPoll.refresh();
  }, [vendorPoll, infraPoll]);

  const lastUpdated =
    [vendorPoll.lastUpdated, infraPoll.lastUpdated]
      .filter((d): d is Date => !!d)
      .sort((a, b) => b.getTime() - a.getTime())[0] ?? null;

  return (
    <div className="space-y-6">
      <header className="flex flex-col gap-2 sm:flex-row sm:items-end sm:justify-between">
        <div>
          <h1 className="text-[1.7rem] font-semibold text-[var(--color-ink-900)]">
            System status
          </h1>
          <p className="mt-1 text-[13px] text-[var(--color-ink-500)]">
            DTMS infrastructure + external vendors. Infra is polled from
            <code className="mx-1 rounded bg-[var(--color-ink-100)] px-1 text-[11px]">/health/ready</code>
            every 30s; vendor transitions arrive over SignalR.
          </p>
        </div>
        <div className="flex items-center gap-2">
          <span
            className={cn(
              "inline-flex items-center gap-1.5 rounded-full px-2.5 py-1 text-[11px] font-medium",
              liveConnected
                ? "bg-[var(--color-success)]/10 text-[var(--color-success)]"
                : "bg-[var(--color-ink-100)] text-[var(--color-ink-500)]",
            )}
            title={liveConnected ? "Vendor updates live via SignalR" : "Reconnecting…"}
          >
            <StatusPulse tone={liveConnected ? "success" : "amber"} size="sm" />
            {liveConnected ? "Live" : "Polling"}
          </span>
          <button
            type="button"
            onClick={refresh}
            className="inline-flex items-center gap-1.5 rounded-full border border-[var(--color-ink-100)] bg-white/70 px-3 py-1.5 text-[11.5px] font-medium text-[var(--color-ink-600)] hover:bg-white dark:bg-white/[0.05] dark:hover:bg-white/[0.12]"
          >
            <RefreshCw className={cn("h-3 w-3", loading && "animate-spin")} strokeWidth={2.4} />
            Refresh
          </button>
        </div>
      </header>

      <section className="grid grid-cols-2 md:grid-cols-4 gap-3">
        <SummaryTile icon={<Activity className="h-4 w-4" />} label="Components" value={summary.total} />
        <SummaryTile
          icon={<CircleCheck className="h-4 w-4" />}
          label="Healthy"
          value={summary.healthy}
          tone="mint"
        />
        <SummaryTile
          icon={<AlertTriangle className="h-4 w-4" />}
          label="Degraded"
          value={summary.degraded}
          tone="amber"
        />
        <SummaryTile
          icon={<AlertCircle className="h-4 w-4" />}
          label="Unhealthy"
          value={summary.unhealthy}
          tone="coral"
        />
      </section>

      {vendorPoll.error && (
        <div className="rounded-xl bg-[#fde0db] px-3 py-2 text-[11.5px] font-medium text-[var(--color-coral)] dark:bg-[#3a1a17]">
          Vendor health: {vendorPoll.error}
        </div>
      )}
      {infraPoll.error && (
        <div className="rounded-xl bg-[#fde0db] px-3 py-2 text-[11.5px] font-medium text-[var(--color-coral)] dark:bg-[#3a1a17]">
          Infrastructure: {infraPoll.error}
        </div>
      )}

      {/* ── Infrastructure ──────────────────────────────────────── */}
      <section>
        <SectionHeader title="Infrastructure" subtitle={`${infraChecks.length} component(s) · readiness probe`} />
        <div className="mt-3 grid grid-cols-1 lg:grid-cols-2 gap-4">
          {infraChecks.length === 0 && !infraPoll.loading && !infraPoll.error && (
            <GlassCard variant="default" className="p-6 col-span-full text-center text-[12px] text-[var(--color-ink-400)]">
              No infrastructure checks reported.
            </GlassCard>
          )}
          {infraChecks.map((c) => (
            <InfraCard key={c.name} check={c} />
          ))}
        </div>
      </section>

      {/* ── External vendors ────────────────────────────────────── */}
      <section>
        <SectionHeader
          title="External vendors"
          subtitle={`${vendors.length} vendor(s) · backend pollers + state machine debounce`}
        />
        <div className="mt-3 grid grid-cols-1 lg:grid-cols-2 gap-4">
          {vendors.length === 0 && !vendorPoll.loading && !vendorPoll.error && (
            <GlassCard variant="default" className="p-6 col-span-full text-center text-[12px] text-[var(--color-ink-400)]">
              No vendor health snapshots yet — backend poller may still be initializing.
            </GlassCard>
          )}
          {vendors.map((v) => (
            <VendorCard key={v.vendor} snapshot={v} />
          ))}
        </div>
      </section>

      {lastUpdated && (
        <p className="text-[11px] text-[var(--color-ink-400)]">
          REST snapshot last refreshed at {lastUpdated.toLocaleTimeString()} · auto-poll every 30s.
        </p>
      )}
    </div>
  );
}

// ────────────────────────────────────────────────────────────────────

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
};

function SummaryTile({ icon, label, value, tone }: SummaryTileProps) {
  const variant =
    tone === "mint" ? "pastel-mint"
    : tone === "amber" ? "pastel-peach"
    : tone === "coral" ? "pastel-lavender"
    : "default";
  return (
    <GlassCard variant={variant as "default" | "pastel-mint" | "pastel-peach" | "pastel-lavender"} className="p-4">
      <div className="flex items-center gap-2 text-[11px] font-medium uppercase tracking-wider text-[var(--color-ink-500)]">
        {icon}
        {label}
      </div>
      <div className="mt-1 text-[1.6rem] font-semibold text-[var(--color-ink-900)]">
        {value.toLocaleString()}
      </div>
    </GlassCard>
  );
}

// ────────────────────────────────────────────────────────────────────

function InfraCard({ check }: { check: InfraCheck }) {
  const meta = statusMeta(check.status);
  return (
    <GlassCard variant="default" className="p-5">
      <div className="flex items-start justify-between gap-3">
        <div>
          <div className="flex items-center gap-2">
            <StatusPulse tone={meta.tone} size="md" />
            <span className="text-[15px] font-semibold uppercase tracking-wider text-[var(--color-ink-900)]">
              {check.name}
            </span>
          </div>
          <div className={cn("mt-0.5 text-[12.5px] font-medium", meta.textClass)}>
            {check.status}
          </div>
        </div>
        <meta.Icon className={cn("h-5 w-5", meta.textClass)} strokeWidth={2.2} />
      </div>
      {check.description && (
        <div className="mt-3 text-[12.5px] text-[var(--color-ink-700)] dark:text-[var(--color-ink-200)]">
          {check.description}
        </div>
      )}
      {check.error && (
        <div className="mt-3 rounded-lg bg-[var(--color-coral)]/10 px-3 py-2 text-[11.5px] text-[var(--color-coral)]">
          {check.error}
        </div>
      )}
      {!check.description && !check.error && (
        <div className="mt-3 text-[11.5px] text-[var(--color-ink-400)]">
          Reporting healthy — no detail provided by the probe.
        </div>
      )}
    </GlassCard>
  );
}

// ────────────────────────────────────────────────────────────────────

function VendorCard({ snapshot }: { snapshot: VendorHealthSnapshot }) {
  const meta = statusMeta(snapshot.status);

  return (
    <GlassCard variant="default" className="p-5">
      <div className="flex items-start justify-between gap-3">
        <div>
          <div className="flex items-center gap-2">
            <StatusPulse tone={meta.tone} size="md" />
            <span className="text-[15px] font-semibold uppercase tracking-wider text-[var(--color-ink-900)]">
              {snapshot.vendor}
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
          value={relativeTime(snapshot.lastCheckedAt)}
          title={new Date(snapshot.lastCheckedAt).toLocaleString()}
        />
        <Field
          label="Last changed"
          value={relativeTime(snapshot.lastChangedAt)}
          title={new Date(snapshot.lastChangedAt).toLocaleString()}
        />
        {snapshot.code && (
          <Field
            label="Code"
            value={snapshot.message ? `${snapshot.code} · ${snapshot.message}` : snapshot.code}
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

type FieldProps = { label: string; value: string; title?: string };
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

function statusMeta(status: AnyStatus): StatusMeta {
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

function relativeTime(iso: string): string {
  if (!iso) return "—";
  const then = new Date(iso).getTime();
  if (Number.isNaN(then)) return "—";
  const seconds = Math.round((Date.now() - then) / 1000);
  if (seconds < 5) return "just now";
  if (seconds < 60) return `${seconds}s ago`;
  const minutes = Math.round(seconds / 60);
  if (minutes < 60) return `${minutes}m ago`;
  const hours = Math.round(minutes / 60);
  if (hours < 24) return `${hours}h ago`;
  const days = Math.round(hours / 24);
  return `${days}d ago`;
}
