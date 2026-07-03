"use client";

import {
  Bot,
  Box,
  Calendar,
  Clock,
  Hash,
  Layers,
  X,
} from "lucide-react";
import { AnimatePresence, motion } from "motion/react";
import { useEffect, useState } from "react";
import {
  getTripDetails,
  isTripInFlight,
  type TripDetailsDto,
  type TripMissionDto,
} from "@/lib/api/trips";
import { cn } from "@/lib/utils";
import { StatusTimelineSection } from "@/components/projection/status-timeline-section";
import type { StatusHistoryEntry } from "@/lib/api/status-history";
import { getTripStatusHistory } from "@/lib/api/status-history";
import { useTripSubscription } from "@/lib/realtime/hubs/trip-hub";
import { AttemptBadge, RetryChainNav, TripStatusBadge } from "./badges";
import {
  MissionFailureAlert,
  MISSION_TIMELINE_ANCHOR,
  getFailingMissions,
} from "./mission-failure-alert";
import { MissionTimeline } from "./mission-timeline";
import { RetryHistoryPanel } from "./retry-history-panel";
import { SnapshotInspector } from "./snapshot-inspector";
import { TripActionBar } from "./trip-action-bar";
import { DateTime } from "@/components/primitives/date-time";
import { TripItemsSection } from "./trip-items-section";
import { OmsNotificationSection } from "@/components/delivery-orders/oms-notification-section";

// Slide-in drawer over the order detail drawer. Mirrors the existing
// order drawer pattern (backdrop + spring transition + escape-to-close)
// so operators get consistent muscle memory across the cockpit.
export function TripDetailDrawer({
  tripId,
  onClose,
  onOpenTrip,
  onOpenOrder,
}: {
  tripId: string | null;
  onClose: () => void;
  // Lets the drawer hand control to a sibling Trip (e.g. previous retry
  // attempt) without forcing a parent re-render.
  onOpenTrip?: (id: string) => void;
  // Phase P5.3 — clicking an OrderRef in the trip items table opens
  // the Order drawer stacked on top of this one. Parent (e.g.
  // orders-experience) wires the state.
  onOpenOrder?: (orderId: string) => void;
}) {
  const [data, setData] = useState<TripDetailsDto | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  // Compliance toggle — when true we re-fetch with includeRaw=true so the
  // snapshot inspectors get the full vendor JSON. Off by default to keep
  // the network footprint light.
  const [includeRaw, setIncludeRaw] = useState(false);
  // Phase P1 — TripHub live timeline updates while the drawer is open.
  const [liveTimelineEntry, setLiveTimelineEntry] = useState<StatusHistoryEntry | null>(null);
  // Mission events pushed over TripHub.MissionUpdated since the drawer
  // opened. Kept separate from data.missions so a refetch (e.g. user
  // toggles "Include raw") doesn't drop in-flight pushes. Merged with
  // data.missions at render time; dedup by (missionKey, state) matches
  // the backend's unique index, so a webhook + reconciler racing to
  // upsert the same row only produces one timeline entry.
  const [liveMissionEvents, setLiveMissionEvents] = useState<TripMissionDto[]>([]);

  useTripSubscription(tripId, {
    TimelineUpdated: (entry) => setLiveTimelineEntry(entry as StatusHistoryEntry),
    MissionUpdated: (event) =>
      setLiveMissionEvents((prev) => {
        const incoming = event as TripMissionDto;
        if (prev.some((m) => m.missionKey === incoming.missionKey && m.state === incoming.state)) {
          return prev; // already seen this (missionKey, state) — dedup
        }
        return [...prev, incoming];
      }),
  });

  useEffect(() => {
    // Reset live pushes whenever the drawer switches to a different
    // trip — otherwise events from the previous trip would leak into
    // the new one. Reset on every effect run (not just tripId === null)
    // so reopening the same trip after a close also starts clean.
    setLiveMissionEvents([]);
    setLiveTimelineEntry(null);
    if (!tripId) {
      setData(null);
      setError(null);
      return;
    }
    let cancelled = false;
    setLoading(true);
    setError(null);

    getTripDetails(tripId, { includeRaw })
      .then((d) => {
        if (!cancelled) setData(d);
      })
      .catch((e: Error) => {
        if (!cancelled) setError(e.message);
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [tripId, includeRaw]);

  useEffect(() => {
    if (!tripId) return;
    const onKey = (e: KeyboardEvent) => e.key === "Escape" && onClose();
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [tripId, onClose]);

  const refresh = () => {
    if (!tripId) return;
    getTripDetails(tripId, { includeRaw }).then(setData).catch(() => undefined);
  };

  return (
    <AnimatePresence>
      {tripId && (
        <>
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            transition={{ duration: 0.25 }}
            onClick={onClose}
            className="fixed inset-0 z-[60] bg-[var(--color-ink-900)]/40 backdrop-blur-sm"
          />
          <motion.aside
            initial={{ x: "100%" }}
            animate={{ x: 0 }}
            exit={{ x: "100%" }}
            transition={{ type: "spring", stiffness: 340, damping: 38 }}
            className={cn(
              "fixed right-0 top-0 z-[70] flex h-full w-full flex-col overflow-hidden",
              "sm:w-[min(640px,100vw)]",
              "bg-[var(--color-surface)] dark:bg-[var(--color-surface)]",
              "shadow-[0_30px_80px_-20px_rgba(15,23,42,0.5)]",
            )}
          >
            <header className="flex items-start justify-between gap-3 border-b border-[var(--color-ink-100)] px-6 py-5 dark:border-white/[0.06]">
              <div className="min-w-0 flex-1">
                <div className="text-[10.5px] font-semibold uppercase tracking-[0.12em] text-[var(--color-ink-400)]">
                  Trip
                </div>
                {loading && !data ? (
                  <div className="mt-2 h-7 w-40 animate-pulse rounded-md bg-[var(--color-ink-100)]" />
                ) : (
                  <h2 className="mt-1 font-mono text-[1.3rem] font-semibold text-[var(--color-ink-900)] truncate">
                    {data?.vendorOrderKey ?? data?.upperKey?.slice(0, 18) ?? tripId.slice(0, 8)}
                  </h2>
                )}
                {data && (
                  <div className="mt-2 flex flex-wrap items-center gap-2">
                    <TripStatusBadge status={data.status} size="md" />
                    <AttemptBadge attempt={data.attemptNumber} />
                    <RetryChainNav
                      attempt={data.attemptNumber}
                      previousAttemptId={data.previousAttemptId}
                      onOpenPrevious={onOpenTrip}
                    />
                  </div>
                )}
              </div>
              <button
                type="button"
                onClick={onClose}
                className="rounded-full p-2 text-[var(--color-ink-500)] transition-colors hover:bg-[var(--color-ink-100)] hover:text-[var(--color-ink-900)] dark:hover:bg-white/10"
                aria-label="Close trip detail"
              >
                <X className="h-4 w-4" strokeWidth={2.4} />
              </button>
            </header>

            <div className="flex-1 overflow-y-auto px-6 py-5">
              {error && (
                <div className="rounded-xl bg-[var(--color-coral-soft)] px-4 py-3 text-[12.5px] font-medium text-[var(--color-coral)]">
                  {error}
                </div>
              )}

              {loading && !data && (
                <div className="space-y-3">
                  {Array.from({ length: 5 }).map((_, i) => (
                    <div
                      key={i}
                      className="h-14 animate-pulse rounded-xl bg-[var(--color-ink-100)] dark:bg-white/[0.05]"
                    />
                  ))}
                </div>
              )}

              {data && (() => {
                // Fold SignalR pushes into the fetched missions list so
                // the banner + chip + Mission Timeline reflect new vendor
                // events without waiting for a manual refresh. Dedup by
                // (missionKey, state) matches both the backend unique
                // index and the live-push dedup, so a webhook arriving
                // microseconds after a fetch is harmless.
                const seen = new Set(data.missions.map((m) => `${m.missionKey}|${m.state}`));
                const mergedMissions = [
                  ...data.missions,
                  ...liveMissionEvents.filter((m) => !seen.has(`${m.missionKey}|${m.state}`)),
                ];
                // Compute once per render so the alert banner, the Status
                // Timeline "N issues" chip, and the Resume button's warning
                // style all stay in sync from the same source of truth.
                const failingMissions = getFailingMissions(mergedMissions);
                return (
                <motion.div
                  initial={{ opacity: 0, y: 8 }}
                  animate={{ opacity: 1, y: 0 }}
                  transition={{ duration: 0.4 }}
                  className="space-y-6"
                >
                  {data.failureReason && (
                    <div className="rounded-xl bg-[var(--color-coral-soft)] px-4 py-3 text-[12.5px] font-medium text-[var(--color-coral)]">
                      <div className="text-[10.5px] font-semibold uppercase tracking-[0.06em] opacity-80">
                        Failure reason
                      </div>
                      <div className="mt-1">{data.failureReason}</div>
                    </div>
                  )}

                  {/* Surfaces vendor-side mission failures (FAILED / HANG /
                      REJECTED) at the top of the drawer so operators see
                      them before reaching for Pause/Resume. Auto-hides
                      when no missions are in alerting states. */}
                  <MissionFailureAlert missions={mergedMissions} />

                  {/* Phase P1 — structured status-history timeline with
                      realtime TripHub updates. */}
                  <StatusTimelineSection
                    entityId={data.id}
                    fetcher={getTripStatusHistory}
                    liveEntry={liveTimelineEntry}
                    issueCount={failingMissions.length}
                  />

                  {/* Mission timeline sits right under the Status timeline
                      so operators see vendor-side failures inline with
                      DTMS state transitions. Anchor id is the target of
                      the MissionFailureAlert scroll-to-anchor button. */}
                  <section id={MISSION_TIMELINE_ANCHOR}>
                    <SectionTitle>Mission timeline ({mergedMissions.length})</SectionTitle>
                    <MissionTimeline missions={mergedMissions} />
                  </section>

                  {/* Upstream-OMS shipment notification status. Auto-hides
                      when the order is internal (no OMS rows in audit).
                      Scoped to this trip — Resend buttons target this
                      trip's id directly. */}
                  <OmsNotificationSection
                    orderId={data.deliveryOrderId}
                    tripId={data.id}
                  />

                  <section className="grid grid-cols-2 gap-3">
                    <MetaCell
                      icon={<Layers className="h-3 w-3" strokeWidth={2.2} />}
                      label="Template"
                      value={data.templateNameAtDispatch ?? "—"}
                    />
                    <MetaCell
                      icon={<Hash className="h-3 w-3" strokeWidth={2.2} />}
                      label="Priority"
                      value={data.priorityAtDispatch?.toString() ?? "—"}
                    />
                    <MetaCell
                      icon={<Bot className="h-3 w-3" strokeWidth={2.2} />}
                      label="Vehicle / Operator"
                      // AMR trips → robot (vendorVehicle*); Manual pool trips
                      // carry no vehicle, so fall back to the claiming
                      // operator's name. "— not assigned" only when neither.
                      value={
                        data.vendorVehicleName ??
                        data.vendorVehicleKey ??
                        data.claimedByOperatorName ??
                        "— not assigned"
                      }
                      mono={!data.vendorVehicleName && !!data.vendorVehicleKey}
                      hint={data.vendorVehicleName && data.vendorVehicleKey ? data.vendorVehicleKey : undefined}
                    />
                    <MetaCell
                      icon={<Box className="h-3 w-3" strokeWidth={2.2} />}
                      label="Upper key"
                      value={data.upperKey}
                      mono
                    />
                    <MetaCell
                      icon={<Calendar className="h-3 w-3" strokeWidth={2.2} />}
                      label="Created"
                      value={<DateTime value={data.createdAt} />}
                    />
                    <MetaCell
                      icon={<Clock className="h-3 w-3" strokeWidth={2.2} />}
                      label={data.completedAt ? "Completed" : data.startedAt ? "Started" : "ETA"}
                      value={
                        <DateTime
                          value={
                            data.completedAt ??
                            data.startedAt ??
                            data.vendorExpectedCompletionAt
                          }
                        />
                      }
                    />
                  </section>

                  {/* Phase P5.3 — items bound to this trip + each
                      item's owning order context. Clicking an OrderRef
                      opens the Order drawer stacked on top. */}
                  <TripItemsSection tripId={data.id} onOpenOrder={onOpenOrder} />

                  {isTripInFlight(data.status) && (
                    <section>
                      <SectionTitle>Operator actions</SectionTitle>
                      <TripActionBar
                        tripId={data.id}
                        status={data.status}
                        vendorVehicleKey={data.vendorVehicleKey}
                        hasVendorIssue={failingMissions.length > 0}
                        onAction={(action, payload) => {
                          if (action === "retry" && payload?.newTripId && onOpenTrip) {
                            onOpenTrip(payload.newTripId);
                          } else {
                            refresh();
                          }
                        }}
                      />
                    </section>
                  )}

                  {/* Retry action is on the Cancelled action bar (in-flight states only),
                      but cancelled trips also need to expose Retry. Surface it here. */}
                  {data.status === "Cancelled" && (
                    <section>
                      <SectionTitle>Operator actions</SectionTitle>
                      <TripActionBar
                        tripId={data.id}
                        status={data.status}
                        vendorVehicleKey={data.vendorVehicleKey}
                        hasVendorIssue={failingMissions.length > 0}
                        onAction={(action, payload) => {
                          if (action === "retry" && payload?.newTripId && onOpenTrip) {
                            onOpenTrip(payload.newTripId);
                          } else {
                            refresh();
                          }
                        }}
                      />
                    </section>
                  )}

                  {/* Retry history — shows only when this trip is part of a
                      chain (i.e. there are 2+ attempts for its group). */}
                  {data.attemptNumber > 1 || data.previousAttemptId ? (
                    <section>
                      <SectionTitle>Retry history</SectionTitle>
                      <RetryHistoryPanel tripId={data.id} onOpenAttempt={onOpenTrip} />
                    </section>
                  ) : null}

                  <section>
                    <div className="mb-2 flex items-center justify-between">
                      <SectionTitle className="mb-0">Vendor snapshots</SectionTitle>
                      <label className="flex cursor-pointer items-center gap-1.5 text-[10.5px] font-semibold uppercase tracking-[0.06em] text-[var(--color-ink-500)]">
                        <input
                          type="checkbox"
                          checked={includeRaw}
                          onChange={(e) => setIncludeRaw(e.target.checked)}
                          className="h-3 w-3 accent-[var(--color-brand-500)]"
                        />
                        Include raw
                      </label>
                    </div>
                    <div className="space-y-2">
                      <SnapshotInspector
                        label="Request (what DTMS sent)"
                        payload={data.vendorRequestSnapshot}
                      />
                      <SnapshotInspector
                        label="Final (vendor's last word)"
                        payload={data.vendorFinalSnapshot}
                      />
                    </div>
                  </section>
                </motion.div>
                );
              })()}
            </div>
          </motion.aside>
        </>
      )}
    </AnimatePresence>
  );
}

function SectionTitle({
  children,
  className,
}: {
  children: React.ReactNode;
  className?: string;
}) {
  return (
    <h3
      className={cn(
        "mb-2 text-[10.5px] font-semibold uppercase tracking-[0.12em] text-[var(--color-ink-400)]",
        className,
      )}
    >
      {children}
    </h3>
  );
}

function MetaCell({
  icon,
  label,
  value,
  mono = false,
  hint,
}: {
  icon: React.ReactNode;
  label: string;
  value: React.ReactNode;
  mono?: boolean;
  // Secondary line under `value` — used to show the raw vendor key
  // when a friendly label is available, so the operator can still
  // copy the correlation id if needed.
  hint?: string;
}) {
  return (
    <div className="rounded-xl bg-[var(--color-ink-100)]/40 px-3 py-2.5 dark:bg-white/[0.03]">
      <div className="flex items-center gap-1.5 text-[10.5px] font-semibold uppercase tracking-[0.06em] text-[var(--color-ink-400)]">
        {icon}
        {label}
      </div>
      <div
        className={cn(
          "mt-1 text-[12.5px] text-[var(--color-ink-900)] truncate",
          mono && "font-mono",
        )}
        title={
          typeof value === "string"
            ? hint
              ? `${value} (${hint})`
              : value
            : undefined
        }
      >
        {value}
      </div>
      {hint && (
        <div
          className="mt-0.5 font-mono text-[10px] text-[var(--color-ink-400)] truncate"
          title={hint}
        >
          {hint}
        </div>
      )}
    </div>
  );
}
