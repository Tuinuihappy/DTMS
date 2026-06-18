"use client";

import { AlertTriangle, CheckCircle2, CloudUpload, Loader2, RefreshCw, XCircle } from "lucide-react";
import { motion } from "motion/react";
import { useCallback, useEffect, useMemo, useState } from "react";
import {
  getFullOrderAudit,
  resendOmsArrivedNotification,
  resendOmsNotification,
  type FullAuditEntryDto,
} from "@/lib/api/delivery-orders";
import { cn } from "@/lib/utils";

/**
 * Per-trip view of the upstream-OMS shipment notifications fired across
 * the trip's lifecycle. Rendered inside the Trip detail drawer so each
 * trip owns its own OMS state — multi-trip orders no longer conflate
 * signals from different trips.
 *
 * 2 stages — every OMS endpoint DTMS calls today:
 *
 *   • Started  — POST /api/shipments                  (TASK_PROCESSING)
 *   • Arrived  — POST /api/shipments/{id}/arrived     (SUB_TASK_FINISHED @ drop)
 *
 * Audit entries are filtered to those whose RelatedTripId matches the
 * trip in scope. Each stage independently shows Notified / Failed-after-retries
 * / Stale (trigger fired but no audit) / Empty. Resend button appears on
 * failed/stale and posts to the stage's resend endpoint; OMS dedupes by
 * shipmentId so re-firing on a row that previously succeeded is safe.
 */

type StageKey = "started" | "arrived";

type StageConfig = {
  label: string;
  notifiedTypes: ReadonlySet<string>;
  resentTypes: ReadonlySet<string>;
  failedTypes: ReadonlySet<string>;
  // TripExecution event types that mark this stage as reached on the
  // vendor side. If the audit shows one of these but no OMS row exists,
  // the stage is "stale" (gated/skipped/lost).
  triggerEventTypes: ReadonlySet<string>;
  emptyTitle: string;
  emptyHint: string;
  staleHint: string;
  resendLabel: string;
};

const STAGES: Record<StageKey, StageConfig> = {
  started: {
    label: "Started",
    notifiedTypes: new Set(["UpstreamOmsNotified"]),
    resentTypes: new Set(["UpstreamOmsManuallyResent"]),
    failedTypes: new Set(["UpstreamOmsNotifyFailed"]),
    triggerEventTypes: new Set(["TripStarted"]),
    emptyTitle: "Awaiting trip start",
    emptyHint: "OMS will be notified when the first trip transitions to InProgress.",
    staleHint: "Trip is InProgress but no OMS audit row exists — kill switch may be off, or the notification was gated.",
    resendLabel: "Resend started",
  },
  arrived: {
    label: "Arrived",
    notifiedTypes: new Set(["UpstreamOmsArrivedNotified"]),
    resentTypes: new Set(["UpstreamOmsArrivedManuallyResent"]),
    failedTypes: new Set(["UpstreamOmsArrivedNotifyFailed"]),
    triggerEventTypes: new Set(["TripDropCompleted"]),
    emptyTitle: "Awaiting drop",
    emptyHint: "OMS will be notified when the trip reaches its drop station.",
    staleHint: "Trip reached drop but no OMS audit row exists — kill switch may be off, or the notification was gated.",
    resendLabel: "Resend arrived",
  },
};

type OmsStatus =
  | { kind: "loading" }
  | { kind: "empty" }
  | { kind: "notified"; at: string; details: string | null }
  | { kind: "failed"; at: string; details: string | null }
  | { kind: "stale"; at: string };

type ResendState =
  | { kind: "idle" }
  | { kind: "sending" }
  | { kind: "error"; message: string };

export function OmsNotificationSection({
  orderId,
  tripId,
}: {
  orderId: string;
  tripId: string;
}) {
  const [entries, setEntries] = useState<FullAuditEntryDto[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [reloadToken, setReloadToken] = useState(0);
  const [startedResend, setStartedResend] = useState<ResendState>({ kind: "idle" });
  const [arrivedResend, setArrivedResend] = useState<ResendState>({ kind: "idle" });

  useEffect(() => {
    let cancelled = false;
    setError(null);
    getFullOrderAudit(orderId)
      .then((d) => {
        if (!cancelled) setEntries(d.entries);
      })
      .catch((e: Error) => {
        if (!cancelled) setError(e.message);
      });
    return () => {
      cancelled = true;
    };
  }, [orderId, reloadToken]);

  const tripEntries = useMemo<FullAuditEntryDto[] | null>(() => {
    if (entries === null) return null;
    return entries.filter((e) => e.relatedTripId === tripId);
  }, [entries, tripId]);

  // Detect orders that don't participate in upstream OMS at all. Internal
  // orders never produce OMS audit rows, so we hide the section for them.
  //
  // Use the order-level audit (not tripEntries) and key off
  // OrderUpstreamIngested — written at ingest, ~hours before any OMS row
  // exists. Otherwise the section stays hidden during the entire window
  // between trip start and the first successful OMS call, exactly when
  // the operator needs to see the stale/retrying state.
  const isOmsRelevant = useMemo(() => {
    if (entries === null) return null;
    return entries.some(
      (e) => OMS_EVENT_TYPES.has(e.eventType) || e.eventType === "OrderUpstreamIngested",
    );
  }, [entries]);

  const refresh = useCallback(() => setReloadToken((t) => t + 1), []);

  const startedStatus = useMemo(
    () => deriveStatus(tripEntries, STAGES.started),
    [tripEntries],
  );
  const arrivedStatus = useMemo(
    () => deriveStatus(tripEntries, STAGES.arrived),
    [tripEntries],
  );

  const makeResendHandler = (
    setState: (s: ResendState) => void,
    api: (orderId: string, tripId: string) => Promise<unknown>,
  ) => async () => {
    setState({ kind: "sending" });
    try {
      await api(orderId, tripId);
      setState({ kind: "idle" });
      refresh();
    } catch (e) {
      setState({ kind: "error", message: e instanceof Error ? e.message : "Resend failed" });
    }
  };

  const onResendStarted = useCallback(
    makeResendHandler(setStartedResend, (o, t) => resendOmsNotification(o, t)),
    [orderId, tripId, refresh],
  );
  const onResendArrived = useCallback(
    makeResendHandler(setArrivedResend, (o, t) => resendOmsArrivedNotification(o, t)),
    [orderId, tripId, refresh],
  );

  if (error) return null;
  if (isOmsRelevant === false) return null;

  return (
    <section>
      <h4 className="text-[10.5px] font-semibold uppercase tracking-[0.12em] text-[var(--color-ink-400)]">
        <span className="inline-flex items-center gap-1.5">
          <CloudUpload className="h-3 w-3" strokeWidth={2.4} />
          Upstream OMS notification
        </span>
      </h4>
      <motion.div
        initial={{ opacity: 0, y: 4 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ duration: 0.28 }}
        className="mt-2 space-y-2"
      >
        <StageRow
          config={STAGES.started}
          status={startedStatus}
          resendTripId={tripId}
          resendState={startedResend}
          onResend={onResendStarted}
        />
        <StageRow
          config={STAGES.arrived}
          status={arrivedStatus}
          resendTripId={tripId}
          resendState={arrivedResend}
          onResend={onResendArrived}
        />
      </motion.div>
    </section>
  );
}

// Every OMS-related event type across both stages. Used to decide
// whether the order participates in upstream OMS at all — if none of
// these ever appear on the trip's audit, the order is internal and the
// section should not render.
const OMS_EVENT_TYPES = new Set<string>([
  ...STAGES.started.notifiedTypes,
  ...STAGES.started.resentTypes,
  ...STAGES.started.failedTypes,
  ...STAGES.arrived.notifiedTypes,
  ...STAGES.arrived.resentTypes,
  ...STAGES.arrived.failedTypes,
]);

// Latest-wins per stage — pick the most recent of notified/resent vs
// failed. A success after a failure overrides (retry recovered). If
// neither OMS event fires but the trigger event did (e.g. TripStarted
// for the started stage), surface as "stale" so ops can investigate.
function deriveStatus(entries: FullAuditEntryDto[] | null, cfg: StageConfig): OmsStatus {
  if (entries === null) return { kind: "loading" };

  let latestOk: FullAuditEntryDto | null = null;
  let latestFail: FullAuditEntryDto | null = null;
  let latestTrigger: FullAuditEntryDto | null = null;

  for (const e of entries) {
    if (cfg.notifiedTypes.has(e.eventType) || cfg.resentTypes.has(e.eventType)) {
      if (!latestOk || e.occurredAt > latestOk.occurredAt) latestOk = e;
    } else if (cfg.failedTypes.has(e.eventType)) {
      if (!latestFail || e.occurredAt > latestFail.occurredAt) latestFail = e;
    } else if (e.source === "TripExecution" && cfg.triggerEventTypes.has(e.eventType)) {
      if (!latestTrigger || e.occurredAt > latestTrigger.occurredAt) latestTrigger = e;
    }
  }

  if (latestOk && (!latestFail || latestOk.occurredAt >= latestFail.occurredAt)) {
    return { kind: "notified", at: latestOk.occurredAt, details: latestOk.details };
  }
  if (latestFail) {
    return { kind: "failed", at: latestFail.occurredAt, details: latestFail.details };
  }
  if (latestTrigger) {
    return { kind: "stale", at: latestTrigger.occurredAt };
  }
  return { kind: "empty" };
}

function StageRow({
  config,
  status,
  resendTripId,
  resendState,
  onResend,
}: {
  config: StageConfig;
  status: OmsStatus;
  resendTripId: string | null;
  resendState: ResendState;
  onResend: () => void;
}) {
  const showResend =
    (status.kind === "failed" || status.kind === "stale") && resendTripId;
  return (
    <div>
      <StatusCard config={config} status={status} />
      {showResend && (
        <div className="mt-1.5 flex items-center justify-end gap-2">
          {resendState.kind === "error" && (
            <span
              className="text-[10.5px] text-[var(--color-coral)] flex-1 truncate"
              title={resendState.message}
            >
              {resendState.message}
            </span>
          )}
          <button
            type="button"
            onClick={onResend}
            disabled={resendState.kind === "sending"}
            className={cn(
              "inline-flex items-center gap-1.5 rounded-full px-3 py-1.5 text-[11px] font-semibold transition-all",
              "bg-[var(--color-brand-900)] text-white hover:shadow-[0_8px_20px_-8px_rgba(15,23,42,0.5)]",
              "disabled:opacity-50 disabled:cursor-not-allowed",
              "dark:bg-[var(--color-brand-500)] dark:text-[var(--color-ink-50)]",
            )}
          >
            <RefreshCw
              className={cn("h-3 w-3", resendState.kind === "sending" && "animate-spin")}
              strokeWidth={2.6}
            />
            {resendState.kind === "sending" ? "Sending…" : config.resendLabel}
          </button>
        </div>
      )}
    </div>
  );
}

function StageBadge({ children }: { children: React.ReactNode }) {
  return (
    <span className="inline-flex rounded-md bg-white/60 px-1.5 py-[1px] text-[9.5px] font-bold uppercase tracking-[0.08em] text-[var(--color-ink-500)] dark:bg-white/10 dark:text-[var(--color-ink-400)]">
      {children}
    </span>
  );
}

function StatusCard({ config, status }: { config: StageConfig; status: OmsStatus }) {
  if (status.kind === "loading") {
    return (
      <div className="flex items-center gap-2 rounded-xl bg-[var(--color-ink-100)]/40 px-4 py-3 dark:bg-white/[0.04]">
        <StageBadge>{config.label}</StageBadge>
        <Loader2 className="h-3.5 w-3.5 animate-spin text-[var(--color-ink-400)]" strokeWidth={2.4} />
        <span className="text-[12px] text-[var(--color-ink-500)]">Loading…</span>
      </div>
    );
  }

  if (status.kind === "empty") {
    return (
      <div className="rounded-xl bg-[var(--color-ink-100)]/40 px-4 py-3 dark:bg-white/[0.04]">
        <div className="flex items-center gap-2">
          <StageBadge>{config.label}</StageBadge>
          <span className="h-1.5 w-1.5 rounded-full bg-[var(--color-ink-400)]" />
          <span className="text-[12px] font-medium text-[var(--color-ink-700)]">
            {config.emptyTitle}
          </span>
        </div>
        <p className="mt-1 text-[11px] text-[var(--color-ink-400)]">{config.emptyHint}</p>
      </div>
    );
  }

  if (status.kind === "notified") {
    return (
      <div className="rounded-xl bg-[var(--color-success-soft)] px-4 py-3">
        <div className="flex items-start gap-2">
          <CheckCircle2 className="mt-[2px] h-3.5 w-3.5 text-[var(--color-success)]" strokeWidth={2.4} />
          <div className="min-w-0 flex-1">
            <div className="flex items-center gap-2 flex-wrap">
              <StageBadge>{config.label}</StageBadge>
              <span className="text-[12px] font-semibold text-[var(--color-success)]">Notified</span>
              <time
                className="font-mono text-[10.5px] text-[var(--color-success)]/70"
                title={new Date(status.at).toLocaleString()}
              >
                {relativeTime(status.at)}
              </time>
            </div>
            {status.details && (
              <p className="mt-1 break-words font-mono text-[10.5px] text-[var(--color-success)]/80">
                {status.details}
              </p>
            )}
          </div>
        </div>
      </div>
    );
  }

  if (status.kind === "failed") {
    return (
      <div className="rounded-xl bg-[#fde0db] px-4 py-3 dark:bg-[#3a1a17]">
        <div className="flex items-start gap-2">
          <XCircle className="mt-[2px] h-3.5 w-3.5 text-[var(--color-coral)]" strokeWidth={2.4} />
          <div className="min-w-0 flex-1">
            <div className="flex items-center gap-2 flex-wrap">
              <StageBadge>{config.label}</StageBadge>
              <span className="text-[12px] font-semibold text-[var(--color-coral)]">
                Failed after retries
              </span>
              <time
                className="font-mono text-[10.5px] text-[var(--color-coral)]/70"
                title={new Date(status.at).toLocaleString()}
              >
                {relativeTime(status.at)}
              </time>
            </div>
            {status.details && (
              <p className="mt-1 break-words font-mono text-[10.5px] leading-snug text-[var(--color-coral)]/80">
                {status.details}
              </p>
            )}
          </div>
        </div>
      </div>
    );
  }

  // stale
  return (
    <div className="rounded-xl bg-[var(--color-amber-soft)] px-4 py-3">
      <div className="flex items-start gap-2">
        <AlertTriangle className="mt-[2px] h-3.5 w-3.5 text-[var(--color-amber)]" strokeWidth={2.4} />
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-2 flex-wrap">
            <StageBadge>{config.label}</StageBadge>
            <span className="text-[12px] font-semibold text-[var(--color-amber)]">Not sent</span>
            <time
              className="font-mono text-[10.5px] text-[var(--color-amber)]/80"
              title={new Date(status.at).toLocaleString()}
            >
              {relativeTime(status.at)}
            </time>
          </div>
          <p className="mt-1 text-[10.5px] text-[var(--color-amber)]/90">{config.staleHint}</p>
        </div>
      </div>
    </div>
  );
}

function relativeTime(iso: string): string {
  const seconds = Math.round((Date.now() - new Date(iso).getTime()) / 1000);
  if (seconds < 60) return `${seconds}s ago`;
  const min = Math.round(seconds / 60);
  if (min < 60) return `${min}m ago`;
  const hours = Math.round(min / 60);
  if (hours < 48) return `${hours}h ago`;
  const days = Math.round(hours / 24);
  return `${days}d ago`;
}
