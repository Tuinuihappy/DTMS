"use client";

import { Activity, AlertCircle, CircleCheck, Clock, Pause, RefreshCw } from "lucide-react";
import { useCallback } from "react";
import { GlassCard } from "@/components/primitives/glass-card";
import { SectionLabel } from "@/components/primitives/section-label";
import {
  getProjectionStatus,
  type ProjectorRow,
  type ProjectorStatus,
} from "@/lib/api/admin-projections";
import { useProjectionPoll } from "@/lib/hooks/use-projection-poll";
import { cn } from "@/lib/utils";

// CC4 — Projection health dashboard. Cross-module aggregate of
// per-projector ProjectionInbox stats: last processed event, lag,
// total processed count, status badge. Polls every 30s so on-call
// can keep it open without manual refreshes.

export function AdminProjectionsExperience() {
  const fetcher = useCallback(
    (signal: AbortSignal) => getProjectionStatus(signal),
    [],
  );
  const { data, loading, error, refresh, lastUpdated } = useProjectionPoll(fetcher, {
    intervalMs: 30_000,
  });

  return (
    <div className="space-y-6">
      <header className="flex flex-col gap-2 sm:flex-row sm:items-end sm:justify-between">
        <div>
          <h1 className="text-[1.7rem] font-semibold text-[var(--color-ink-900)]">
            Projection health
          </h1>
          <p className="mt-1 text-[13px] text-[var(--color-ink-500)]">
            Per-projector inbox stats aggregated across {data?.modules.length ?? 4} modules.
            Lag = NOW − last processed event timestamp.
          </p>
        </div>
        <button
          type="button"
          onClick={refresh}
          className="inline-flex items-center gap-1.5 rounded-full border border-[var(--color-ink-100)] bg-white/70 px-3 py-1.5 text-[11.5px] font-medium text-[var(--color-ink-600)] hover:bg-white dark:bg-white/[0.05] dark:hover:bg-white/[0.12]"
        >
          <RefreshCw className={cn("h-3 w-3", loading && "animate-spin")} strokeWidth={2.4} />
          Refresh
        </button>
      </header>

      <section className="grid grid-cols-2 md:grid-cols-4 gap-3">
        <SummaryTile
          icon={<Activity className="h-4 w-4" />}
          label="Projectors"
          value={data?.summary.totalProjectors ?? 0}
        />
        <SummaryTile
          icon={<CircleCheck className="h-4 w-4" />}
          label="Healthy"
          value={data?.summary.healthy ?? 0}
          tone="mint"
        />
        <SummaryTile
          icon={<AlertCircle className="h-4 w-4" />}
          label="Stale"
          value={data?.summary.stale ?? 0}
          tone="amber"
        />
        <SummaryTile
          icon={<Pause className="h-4 w-4" />}
          label="Idle"
          value={data?.summary.idle ?? 0}
          tone="ink"
        />
      </section>

      {error && (
        <div className="rounded-xl bg-[#fde0db] px-3 py-2 text-[11.5px] font-medium text-[var(--color-coral)] dark:bg-[#3a1a17]">
          {error}
        </div>
      )}

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
        {(data?.modules ?? []).map((m) => (
          <GlassCard key={m.module} variant="default" className="p-5">
            <SectionLabel
              title={m.module}
              subtitle={`${m.projectors.length} projector(s) · ${m.inboxTotal.toLocaleString()} events processed · schema "${m.schema}"`}
            />

            <div className="mt-3 divide-y divide-[var(--color-ink-100)]/70 dark:divide-white/5">
              {m.projectors.length === 0 && (
                <div className="py-6 text-center text-[12px] text-[var(--color-ink-400)]">
                  No projectors have written to this module's inbox yet.
                </div>
              )}
              {m.projectors.map((p) => (
                <ProjectorRowCard key={p.name} projector={p} />
              ))}
            </div>
          </GlassCard>
        ))}
      </div>

      <footer className="text-[11px] text-[var(--color-ink-400)]">
        Last refreshed{" "}
        {lastUpdated
          ? new Date(lastUpdated).toLocaleTimeString()
          : data?.generatedAtUtc
            ? new Date(data.generatedAtUtc).toLocaleTimeString()
            : "—"}
        {" · "}auto-refresh every 30s
        {" · "}thresholds: stale &gt; 5 min · idle &gt; 1 hr
      </footer>
    </div>
  );
}

function ProjectorRowCard({ projector }: { projector: ProjectorRow }) {
  return (
    <div className="flex items-center justify-between py-2.5 gap-3">
      <div className="min-w-0 flex-1">
        <div className="font-mono text-[12.5px] font-semibold text-[var(--color-ink-800)] dark:text-[var(--color-ink-100)] truncate" title={projector.name}>
          {projector.name}
        </div>
        <div className="mt-0.5 flex items-center gap-2 text-[11px] text-[var(--color-ink-500)]">
          <Clock className="h-3 w-3" strokeWidth={2.2} />
          <span>Last: {formatTimeAgo(projector.lagSeconds)}</span>
          <span className="text-[var(--color-ink-300)]">·</span>
          <span className="font-mono tabular-nums">
            {projector.processed.toLocaleString()} events
          </span>
        </div>
      </div>
      <StatusBadge status={projector.status} />
    </div>
  );
}

function StatusBadge({ status }: { status: ProjectorStatus }) {
  const styles: Record<ProjectorStatus, { label: string; cls: string }> = {
    healthy: {
      label: "Healthy",
      cls: "bg-[var(--color-mint-50,#e7faec)] text-[var(--color-mint-700,#15803d)] dark:bg-[var(--color-mint-500)]/15 dark:text-[var(--color-mint-300,#86efac)]",
    },
    stale: {
      label: "Stale",
      cls: "bg-[var(--color-amber-50,#fff7ed)] text-[var(--color-amber-700,#b45309)] dark:bg-[var(--color-amber-500)]/15 dark:text-[var(--color-amber-300,#fcd34d)]",
    },
    idle: {
      label: "Idle",
      cls: "bg-[var(--color-ink-100)] text-[var(--color-ink-600)] dark:bg-white/[0.07] dark:text-[var(--color-ink-300)]",
    },
  };
  const s = styles[status];
  return (
    <span
      className={cn(
        "shrink-0 rounded-full px-2.5 py-1 text-[10.5px] font-semibold uppercase tracking-[0.08em]",
        s.cls,
      )}
    >
      {s.label}
    </span>
  );
}

function SummaryTile({
  icon,
  label,
  value,
  tone = "ink",
}: {
  icon: React.ReactNode;
  label: string;
  value: number;
  tone?: "ink" | "mint" | "amber";
}) {
  return (
    <GlassCard variant="default" className="p-4">
      <div className="flex items-center gap-1.5 text-[10.5px] font-semibold uppercase tracking-[0.12em] text-[var(--color-ink-500)]">
        <span className="text-[var(--color-ink-400)]">{icon}</span>
        {label}
      </div>
      <div
        className={cn(
          "mt-2 font-mono text-[1.8rem] font-semibold tabular-nums leading-none",
          tone === "mint"
            ? "text-[var(--color-mint-600,#16a34a)]"
            : tone === "amber"
              ? "text-[var(--color-amber-600)]"
              : "text-[var(--color-ink-900)]",
        )}
      >
        {value.toLocaleString()}
      </div>
    </GlassCard>
  );
}

function formatTimeAgo(seconds: number): string {
  if (seconds < 60) return `${seconds}s ago`;
  if (seconds < 3600) return `${Math.floor(seconds / 60)}m ago`;
  if (seconds < 86400) return `${Math.floor(seconds / 3600)}h ago`;
  return `${Math.floor(seconds / 86400)}d ago`;
}
