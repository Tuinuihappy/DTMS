"use client";

import { AlertTriangle, CheckCircle2, CloudUpload, Loader2, RefreshCw, XCircle } from "lucide-react";
import { motion } from "motion/react";
import { useCallback, useEffect, useMemo, useState } from "react";
import {
  getFullOrderAudit,
  resendSourceArrivedNotification,
  resendSourceNotification,
  type FullAuditEntryDto,
} from "@/lib/api/delivery-orders";
import { cn } from "@/lib/utils";
import { DateTime } from "@/components/primitives/date-time";

/**
 * Per-trip view of the upstream source-system callbacks fired across the
 * trip's lifecycle. Rendered inside the Trip detail drawer so each trip owns
 * its own upstream state — multi-trip orders no longer conflate signals from
 * different trips.
 *
 * Phase C (multi-source) — event types are system-NEUTRAL (UpstreamNotified,
 * not UpstreamOmsNotified); which system a row concerns rides in its
 * `systemKey`, and the section header renders that system's name. The same
 * component serves oms/sap/erp orders without change.
 *
 * 3 stages:
 *   • Started   — shipment.started.v1     (fires at trip start)
 *   • Arrived   — shipment.arrived.v1     (fires at drop completed)
 *   • Cancelled — shipment.cancelled.v1   (fires at trip cancel; the stage
 *     renders only when a cancelled callback row actually exists — the
 *     subscription ships disabled, so an always-on row would show a
 *     meaningless "Awaiting" on every order)
 *
 * Audit entries are filtered to those whose RelatedTripId matches the trip
 * in scope. Each stage independently shows Notified / Failed-after-retries /
 * Stale (trigger fired but no callback row) / Empty. Resend button appears on
 * failed/stale (started + arrived only) and posts to the stage's resend
 * endpoint; upstreams dedupe by shipmentId so re-firing is safe.
 */

type StageKey = "started" | "arrived" | "cancelled";

type StageConfig = {
  label: string;
  notifiedTypes: ReadonlySet<string>;
  resentTypes: ReadonlySet<string>;
  failedTypes: ReadonlySet<string>;
  // TripExecution event types that mark this stage as reached on the
  // vendor side. If the audit shows one of these but no callback row
  // exists, the stage is "stale" (gated/skipped/lost).
  triggerEventTypes: ReadonlySet<string>;
  emptyTitle: string;
  emptyHint: string;
  staleHint: string;
  // Null = no manual resend exists for this stage (cancelled).
  resendLabel: string | null;
  // Render the stage only when a callback row (ok/failed) exists — used by
  // cancelled while its subscription ships disabled.
  showOnlyWithData: boolean;
};

// Values mirror UpstreamCallbackAudit on the backend string-for-string —
// change one side and the other (plus a data migration) must move with it.
const STAGES: Record<StageKey, StageConfig> = {
  started: {
    label: "Started",
    notifiedTypes: new Set(["UpstreamNotified"]),
    resentTypes: new Set(["UpstreamManuallyResent"]),
    // Both failure flavours render as the same red "Failed" card (Option A).
    // The audit Details string carries the upstream body so operators can
    // tell them apart inline:
    //   - UpstreamNotifyFailed → transient retries exhausted (upstream down)
    //   - UpstreamRejected     → fast-failed 4xx (bad data, fix upstream)
    failedTypes: new Set(["UpstreamNotifyFailed", "UpstreamRejected"]),
    triggerEventTypes: new Set(["TripStarted"]),
    emptyTitle: "Awaiting trip start",
    emptyHint: "The source system is notified when the first trip transitions to InProgress.",
    staleHint:
      "Trip is InProgress but no callback row exists — the subscription may be disabled, or the notification was gated.",
    resendLabel: "Resend started",
    showOnlyWithData: false,
  },
  arrived: {
    label: "Arrived",
    notifiedTypes: new Set(["UpstreamArrivedNotified"]),
    resentTypes: new Set(["UpstreamArrivedManuallyResent"]),
    failedTypes: new Set(["UpstreamArrivedNotifyFailed", "UpstreamArrivedRejected"]),
    triggerEventTypes: new Set(["TripDropCompleted"]),
    emptyTitle: "Awaiting drop",
    emptyHint: "The source system is notified when the trip reaches its drop station.",
    staleHint:
      "Trip reached drop but no callback row exists — the subscription may be disabled, or the notification was gated.",
    resendLabel: "Resend arrived",
    showOnlyWithData: false,
  },
  cancelled: {
    label: "Cancelled",
    notifiedTypes: new Set(["UpstreamCancelledNotified"]),
    resentTypes: new Set<string>(),
    failedTypes: new Set(["UpstreamCancelledNotifyFailed", "UpstreamCancelledRejected"]),
    triggerEventTypes: new Set(["TripCancelled"]),
    emptyTitle: "",
    emptyHint: "",
    staleHint: "",
    resendLabel: null,
    showOnlyWithData: true,
  },
};

type UpstreamStatus =
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

  // Detect orders that don't participate in upstream callbacks at all.
  // Internal orders never produce callback rows, so hide the section for
  // them. Uses the order-level audit (not tripEntries) and keys off
  // OrderUpstreamIngested — written at ingest, ~hours before any callback
  // row exists — so the section is already visible during the window
  // between trip start and the first callback, exactly when the operator
  // needs to see the stale/retrying state.
  const isUpstreamRelevant = useMemo(() => {
    if (entries === null) return null;
    return entries.some(
      (e) => UPSTREAM_EVENT_TYPES.has(e.eventType) || e.eventType === "OrderUpstreamIngested",
    );
  }, [entries]);

  // The system this order's callbacks concern, straight off the audit rows
  // ('oms' → "OMS"). Falls back to a neutral phrase until a row carries one.
  const systemLabel = useMemo(() => {
    const key = entries?.find((e) => e.systemKey)?.systemKey;
    return key ? key.toUpperCase() : "source system";
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
  const cancelledStatus = useMemo(
    () => deriveStatus(tripEntries, STAGES.cancelled),
    [tripEntries],
  );
  const showCancelled =
    cancelledStatus.kind === "notified" || cancelledStatus.kind === "failed";

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
    makeResendHandler(setStartedResend, (o, t) => resendSourceNotification(o, t)),
    [orderId, tripId, refresh],
  );
  const onResendArrived = useCallback(
    makeResendHandler(setArrivedResend, (o, t) => resendSourceArrivedNotification(o, t)),
    [orderId, tripId, refresh],
  );

  if (error) return null;
  if (isUpstreamRelevant === false) return null;

  return (
    <section>
      <h4 className="text-[10.5px] font-semibold uppercase tracking-[0.12em] text-[var(--color-ink-400)]">
        <span className="inline-flex items-center gap-1.5">
          <CloudUpload className="h-3 w-3" strokeWidth={2.4} />
          Upstream {systemLabel} notification
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
        {showCancelled && (
          <StageRow
            config={STAGES.cancelled}
            status={cancelledStatus}
            resendTripId={null}
            resendState={{ kind: "idle" }}
            onResend={() => {}}
          />
        )}
      </motion.div>
    </section>
  );
}

// Every upstream-callback event type across all stages. Used to decide
// whether the order participates in upstream callbacks at all — if none of
// these ever appear on the order's audit, the order is internal and the
// section should not render.
const UPSTREAM_EVENT_TYPES = new Set<string>([
  ...STAGES.started.notifiedTypes,
  ...STAGES.started.resentTypes,
  ...STAGES.started.failedTypes,
  ...STAGES.arrived.notifiedTypes,
  ...STAGES.arrived.resentTypes,
  ...STAGES.arrived.failedTypes,
  ...STAGES.cancelled.notifiedTypes,
  ...STAGES.cancelled.failedTypes,
]);

// Latest-wins per stage — pick the most recent of notified/resent vs
// failed. A success after a failure overrides (retry recovered). If
// neither callback event fires but the trigger event did (e.g. TripStarted
// for the started stage), surface as "stale" so ops can investigate.
function deriveStatus(entries: FullAuditEntryDto[] | null, cfg: StageConfig): UpstreamStatus {
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
  status: UpstreamStatus;
  resendTripId: string | null;
  resendState: ResendState;
  onResend: () => void;
}) {
  const showResend =
    (status.kind === "failed" || status.kind === "stale") &&
    resendTripId &&
    config.resendLabel !== null;
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

function StatusCard({ config, status }: { config: StageConfig; status: UpstreamStatus }) {
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
              <DateTime
                value={status.at}
                variant="relative"
                className="font-mono text-[10.5px] text-[var(--color-success)]/70"
              />
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
      <div className="rounded-xl bg-[var(--color-coral-soft)] px-4 py-3">
        <div className="flex items-start gap-2">
          <XCircle className="mt-[2px] h-3.5 w-3.5 text-[var(--color-coral)]" strokeWidth={2.4} />
          <div className="min-w-0 flex-1">
            <div className="flex items-center gap-2 flex-wrap">
              <StageBadge>{config.label}</StageBadge>
              <span className="text-[12px] font-semibold text-[var(--color-coral)]">
                Failed after retries
              </span>
              <DateTime
                value={status.at}
                variant="relative"
                className="font-mono text-[10.5px] text-[var(--color-coral)]/70"
              />
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
            <DateTime
              value={status.at}
              variant="relative"
              className="font-mono text-[10.5px] text-[var(--color-amber)]/80"
            />
          </div>
          <p className="mt-1 text-[10.5px] text-[var(--color-amber)]/90">{config.staleHint}</p>
        </div>
      </div>
    </div>
  );
}
