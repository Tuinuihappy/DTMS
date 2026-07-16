"use client";

import {
  ArrowRight,
  Ban,
  Box,
  Calendar,
  ChevronRight,
  Clock,
  Copy,
  Hash,
  History,
  PauseCircle,
  PlayCircle,
  RotateCcw,
  Send,
  Trash2,
  Truck,
  User,
  X,
} from "lucide-react";
import { AnimatePresence, motion } from "motion/react";
import { useEffect, useState } from "react";
import {
  getOrder,
  getOrderTimeline,
  type DeliveryOrderDetailDto,
  type ItemStatus,
  type TimelineEntryDto,
} from "@/lib/api/delivery-orders";
import { getTripsByOrder, type TripSummaryDto } from "@/lib/api/trips";
import { getJobsByOrder, type JobDto } from "@/lib/api/jobs";
import { AttemptBadge, TripStatusBadge } from "@/components/dispatch/badges";
import { JobStatusBadge } from "@/components/planning/badges";
import { TripDetailDrawer } from "@/components/dispatch/trip-detail-drawer";
import { FullAuditLog } from "./full-audit-log";
import { useOrderHubSubscription } from "@/lib/realtime/hubs/order-hub";
import type { FullAuditEntryDto } from "@/lib/api/delivery-orders";
import { cn } from "@/lib/utils";
import { DateTime } from "@/components/primitives/date-time";
import { PriorityBadge, StatusBadge, TransportModeBadge } from "./badges";

type Action =
  | "submit"
  | "delete"
  | "reopen"
  | "redispatch"
  | "hold"
  | "release"
  | "reject"
  | "abandon"
  | "reorder";

const ORDER_IN_FLIGHT_STATES = [
  "Confirmed",
  "Planning",
  "Planned",
  "Dispatched",
  "InProgress",
];
const ACTIVE_TRIP_STATES = ["Created", "InProgress", "Paused"];

export function OrderDetailDrawer({
  orderId,
  onClose,
  onAction,
  onPodScan,
  onOpenOrder,
}: {
  orderId: string | null;
  onClose: () => void;
  onAction: (a: Action, id: string) => Promise<void> | void;
  onPodScan?: (itemId: string, itemLabel: string) => void;
  // Phase P5.3 — clicking an OrderRef inside the stacked Trip drawer
  // hops to that order. Parent (orders-experience) wires it to
  // setDetailId so this drawer simply switches what it's showing
  // rather than spawning yet another stacked instance.
  onOpenOrder?: (id: string) => void;
}) {
  const [data, setData] = useState<DeliveryOrderDetailDto | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  // Timeline loads in parallel with detail. It's allowed to fail on its
  // own (just hide the section) without blocking the drawer from
  // rendering — most of the value is in the items list above.
  const [timeline, setTimeline] = useState<TimelineEntryDto[] | null>(null);
  // Trips dispatched against this order (one per group × attempt). Soft-fail
  // like timeline — the rest of the drawer still works without it.
  const [trips, setTrips] = useState<TripSummaryDto[] | null>(null);
  // Planning Jobs anchoring the order's station-pair groups. One Job per
  // group, status mirrors Trip lifecycle (Phase b9). Soft-fail.
  const [jobs, setJobs] = useState<JobDto[] | null>(null);
  // Currently-open Trip detail drawer (stacks above this drawer).
  const [openTripId, setOpenTripId] = useState<string | null>(null);
  // Phase P2 (Option A) — latest activity entry pushed via OrderHub.
  // FullAuditLog dedup-merges by Id (deterministic = EventId, so live +
  // REST refresh land the same row). StatusTimelineSection was retired
  // here because FullAuditLog covers all categories including status.
  const [liveActivityEntry, setLiveActivityEntry] = useState<FullAuditEntryDto | null>(null);

  useOrderHubSubscription(orderId, {
    ActivityUpdated: (entry) => setLiveActivityEntry(entry as FullAuditEntryDto),
  });

  useEffect(() => {
    if (!orderId) {
      setData(null);
      setTimeline(null);
      setTrips(null);
      setJobs(null);
      setError(null);
      return;
    }
    let cancelled = false;
    setLoading(true);
    setError(null);
    setTimeline(null);
    setTrips(null);
    setJobs(null);

    getOrder(orderId)
      .then((d) => {
        if (!cancelled) setData(d);
      })
      .catch((e: Error) => {
        if (!cancelled) setError(e.message);
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });

    getOrderTimeline(orderId)
      .then((t) => {
        if (!cancelled) setTimeline(t);
      })
      .catch(() => {
        // Soft-fail: leave timeline as null, the section just hides.
      });

    getTripsByOrder(orderId)
      .then((t) => {
        if (!cancelled) setTrips(t);
      })
      .catch(() => {
        // Soft-fail too — the Trips section just hides.
      });

    getJobsByOrder(orderId)
      .then((j) => {
        if (!cancelled) setJobs(j);
      })
      .catch(() => {
        // Soft-fail. Legacy pre-b8 orders genuinely have no Jobs;
        // hiding the section is the right behavior.
      });

    return () => {
      cancelled = true;
    };
  }, [orderId]);

  useEffect(() => {
    if (!orderId) return;
    const onKey = (e: KeyboardEvent) => e.key === "Escape" && onClose();
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [orderId, onClose]);

  const can = (s: string, list: string[]) => list.includes(s);

  return (
    <AnimatePresence>
      {orderId && (
        <>
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            transition={{ duration: 0.25 }}
            onClick={onClose}
            className="fixed inset-0 z-40 bg-[var(--color-ink-900)]/40 backdrop-blur-sm"
          />
          <motion.aside
            initial={{ x: "100%" }}
            animate={{ x: 0 }}
            exit={{ x: "100%" }}
            transition={{ type: "spring", stiffness: 340, damping: 38 }}
            className={cn(
              "fixed right-0 top-0 z-50 flex h-full w-full flex-col overflow-hidden",
              "sm:w-[min(560px,100vw)]",
              "bg-[var(--color-surface)] dark:bg-[var(--color-surface)]",
              "shadow-[0_30px_80px_-20px_rgba(15,23,42,0.5)]",
            )}
          >
            <header className="flex items-start justify-between gap-3 border-b border-[var(--color-ink-100)] px-6 py-5 dark:border-white/[0.06]">
              <div className="min-w-0 flex-1">
                <div className="text-[10.5px] font-semibold uppercase tracking-[0.12em] text-[var(--color-ink-400)]">
                  Delivery order
                </div>
                {loading ? (
                  <div className="mt-2 h-7 w-40 animate-pulse rounded-md bg-[var(--color-ink-100)]" />
                ) : (
                  <h2 className="font-mono mt-1 text-[1.4rem] font-semibold text-[var(--color-ink-900)] truncate">
                    {data?.orderRef ?? "—"}
                  </h2>
                )}
                {data && (
                  <div className="mt-2 flex flex-wrap items-center gap-2">
                    <StatusBadge status={data.orderStatus} size="md" />
                    <PriorityBadge priority={data.priority} />
                    <TransportModeBadge mode={data.requestedTransportMode} />
                    <RequiresDropPodBadge value={data.requiresDropPod} />
                  </div>
                )}
              </div>
              <button
                type="button"
                onClick={onClose}
                className="rounded-full p-2 text-[var(--color-ink-500)] transition-colors hover:bg-[var(--color-ink-100)] hover:text-[var(--color-ink-900)] dark:hover:bg-white/10"
                aria-label="Close detail"
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
                  {Array.from({ length: 6 }).map((_, i) => (
                    <div
                      key={i}
                      className="h-14 animate-pulse rounded-xl bg-[var(--color-ink-100)] dark:bg-white/[0.05]"
                    />
                  ))}
                </div>
              )}

              {data && (
                <motion.div
                  initial={{ opacity: 0, y: 8 }}
                  animate={{ opacity: 1, y: 0 }}
                  transition={{ duration: 0.4 }}
                  className="space-y-6"
                >
                  {/* Meta */}
                  <section className="grid grid-cols-2 gap-3">
                    <MetaCell
                      icon={<Hash className="h-3 w-3" strokeWidth={2.2} />}
                      label="Source"
                      value={data.sourceSystemDisplayName ?? data.sourceSystem}
                    />
                    <MetaCell
                      icon={<User className="h-3 w-3" strokeWidth={2.2} />}
                      label="Requested by"
                      value={data.requestedBy ?? "—"}
                    />
                    <MetaCell
                      icon={<Calendar className="h-3 w-3" strokeWidth={2.2} />}
                      label="Created"
                      value={<DateTime value={data.createdDate} />}
                    />
                    <MetaCell
                      icon={<Clock className="h-3 w-3" strokeWidth={2.2} />}
                      label="Window"
                      value={<DateTime value={data.serviceWindow?.latestUtc} />}
                    />
                  </section>

                  {/* Totals */}
                  <section className="grid grid-cols-3 gap-3">
                    <Totals
                      label="Items"
                      value={data.totalItems}
                      tone="brand"
                    />
                    <Totals
                      label="Quantity"
                      value={data.totalQuantity}
                      tone="ink"
                    />
                    <Totals
                      label="Weight"
                      value={data.totalWeightKg}
                      suffix=" kg"
                      tone="amber"
                    />
                  </section>

                  {/* Phase P2 (Option A, 2026-06-15) — StatusTimelineSection
                      was retired here. FullAuditLog below covers status
                      transitions plus trip events / amendments / POD —
                      one unified timeline with category filter chips. */}

                  {/* Notes */}
                  {data.notes && (
                    <section>
                      <SectionLabel>Notes</SectionLabel>
                      <div className="mt-2 rounded-xl bg-[var(--color-surface-soft)] px-4 py-3 text-[13px] leading-relaxed text-[var(--color-ink-700)] dark:bg-white/[0.04]">
                        {data.notes}
                      </div>
                    </section>
                  )}

                  {/* Planning Jobs anchoring this order's dispatch lineage.
                      One Job per (pickup, drop) station-pair group; status
                      mirrors the bound Trip's lifecycle (Phase b8/b9).
                      Hidden for legacy pre-b8 orders where the consumer
                      didn't create Jobs. */}
                  {jobs && jobs.length > 0 && (
                    <section>
                      <SectionLabel>
                        <span className="inline-flex items-center gap-1.5">
                          <Hash className="h-3 w-3" strokeWidth={2.4} />
                          Planning jobs ({jobs.length})
                        </span>
                      </SectionLabel>
                      <div className="mt-3 space-y-2">
                        {jobs.map((j, i) => (
                          <motion.div
                            key={j.id}
                            initial={{ opacity: 0, x: -6 }}
                            animate={{ opacity: 1, x: 0 }}
                            transition={{ duration: 0.32, delay: i * 0.04 }}
                            className="rounded-xl border border-[var(--color-ink-100)] bg-[var(--color-surface)] px-4 py-3 dark:border-white/[0.05] dark:bg-white/[0.02]"
                          >
                            <div className="flex items-center gap-2 flex-wrap">
                              <JobStatusBadge status={j.status} />
                              {j.groupIndex != null && (
                                <span className="font-mono text-[10.5px] font-semibold text-[var(--color-ink-500)]">
                                  G{j.groupIndex}
                                </span>
                              )}
                              <AttemptBadge attempt={j.attemptNumber} />
                              <span className="font-mono text-[10.5px] text-[var(--color-ink-400)]">
                                {j.id.slice(0, 8)}…
                              </span>
                            </div>
                            {j.failureReason && (
                              <div className="mt-2 rounded-md bg-[var(--color-ink-50)]/60 px-2.5 py-1.5 text-[11.5px] leading-relaxed text-[var(--color-ink-700)] dark:bg-white/[0.03]">
                                <span className="font-semibold uppercase tracking-[0.06em] text-[10px] text-[var(--color-coral)]">
                                  Reason ·
                                </span>{" "}
                                {j.failureReason}
                              </div>
                            )}
                          </motion.div>
                        ))}
                      </div>
                    </section>
                  )}

                  {/* Trips dispatched for this order — drilldown into vendor execution */}
                  {trips && trips.length > 0 && (
                    <section>
                      <SectionLabel>
                        <span className="inline-flex items-center gap-1.5">
                          <Truck className="h-3 w-3" strokeWidth={2.4} />
                          Trips ({trips.length})
                        </span>
                      </SectionLabel>
                      <div className="mt-3 space-y-2">
                        {trips.map((t, i) => (
                          <motion.button
                            key={t.id}
                            type="button"
                            onClick={() => setOpenTripId(t.id)}
                            initial={{ opacity: 0, x: -6 }}
                            animate={{ opacity: 1, x: 0 }}
                            transition={{ duration: 0.32, delay: i * 0.04 }}
                            className="group flex w-full items-center justify-between gap-3 rounded-xl border border-[var(--color-ink-100)] bg-[var(--color-surface)] px-4 py-3 text-left transition-all hover:border-[var(--color-brand-500)]/40 hover:bg-[var(--color-surface-soft)] dark:border-white/[0.05] dark:bg-white/[0.02] dark:hover:bg-white/[0.04]"
                          >
                            <div className="min-w-0 flex-1">
                              <div className="flex items-center gap-2 flex-wrap">
                                <TripStatusBadge status={t.status as never} />
                                <AttemptBadge attempt={t.attemptNumber} />
                                {t.vendorOrderKey && (
                                  <span className="font-mono text-[11.5px] font-semibold text-[var(--color-ink-700)]">
                                    #{t.vendorOrderKey}
                                  </span>
                                )}
                                {t.jobId &&
                                  t.jobId !== "00000000-0000-0000-0000-000000000000" && (
                                    <span
                                      className="inline-flex items-center gap-1 rounded-md bg-[var(--color-pastel-sky)]/40 px-1.5 py-[2px] font-mono text-[10px] font-semibold uppercase tracking-[0.06em] text-[var(--color-pastel-sky-ink)]"
                                      title={`Linked to Job ${t.jobId}`}
                                    >
                                      <Hash className="h-2.5 w-2.5" strokeWidth={2.6} />
                                      Job {t.jobId.slice(0, 8)}…
                                    </span>
                                  )}
                              </div>
                              <div className="mt-1 font-mono text-[10.5px] text-[var(--color-ink-400)] truncate">
                                {t.upperKey}
                              </div>
                            </div>
                            <ChevronRight
                              className="h-4 w-4 flex-shrink-0 text-[var(--color-ink-400)] transition-transform group-hover:translate-x-0.5"
                              strokeWidth={2.4}
                            />
                          </motion.button>
                        ))}
                      </div>
                    </section>
                  )}

                  {/* Full audit log — order events + amendments + per-trip
                      execution + retry triggers, consolidated (Phase 4.2). */}
                  <section>
                    <SectionLabel>
                      <span className="inline-flex items-center gap-1.5">
                        <History className="h-3 w-3" strokeWidth={2.4} />
                        Full audit
                      </span>
                    </SectionLabel>
                    <div className="mt-3">
                      <FullAuditLog
                        orderId={data.id}
                        onOpenTrip={(tripId) => setOpenTripId(tripId)}
                        liveEntry={liveActivityEntry}
                      />
                    </div>
                  </section>


                  {/* Items list with pickup → drop visual */}
                  <section>
                    <SectionLabel>
                      Items ({data.items.length})
                    </SectionLabel>
                    <div className="mt-3 space-y-2">
                      {data.items.map((it, i) => (
                        <motion.div
                          key={it.id}
                          initial={{ opacity: 0, x: -8 }}
                          animate={{ opacity: 1, x: 0 }}
                          transition={{ duration: 0.35, delay: i * 0.04 }}
                          className="rounded-xl border border-[var(--color-ink-100)] bg-[var(--color-surface)] px-4 py-3 dark:border-white/[0.05] dark:bg-white/[0.02]"
                        >
                          <div className="flex items-start justify-between gap-3">
                            <div className="min-w-0 flex-1">
                              <div className="flex items-center gap-2 flex-wrap">
                                <span className="font-mono text-[11px] font-bold text-[var(--color-ink-400)]">
                                  #{it.itemSeq.toString().padStart(2, "0")}
                                </span>
                                <span className="font-mono text-[12.5px] font-semibold text-[var(--color-ink-900)] truncate">
                                  {it.itemId}
                                </span>
                                <ItemStatusBadge status={it.status} />
                                {it.attemptNumber && it.attemptNumber > 1 && (
                                  <span className="rounded bg-[var(--color-pastel-lavender)] px-1.5 py-[2px] text-[9.5px] font-bold uppercase tracking-[0.06em] text-[var(--color-pastel-lavender-ink)]">
                                    attempt {it.attemptNumber}
                                  </span>
                                )}
                                {it.pickupPod && (
                                  <span className="inline-flex items-center gap-1 rounded-full bg-[var(--color-pastel-sky)] px-2 py-[2px] text-[9.5px] font-semibold uppercase tracking-[0.06em] text-[var(--color-pastel-sky-ink)]">
                                    Pickup · {it.pickupPod.method}
                                  </span>
                                )}
                                {it.dropPod && (
                                  <span className="inline-flex items-center gap-1 rounded-full bg-[var(--color-success-soft)] px-2 py-[2px] text-[9.5px] font-semibold uppercase tracking-[0.06em] text-[var(--color-success)]">
                                    Drop · {it.dropPod.method}
                                  </span>
                                )}
                                {data.requiresDropPod === true && it.status === "DroppedOff" && !it.dropPod && (
                                  <button
                                    type="button"
                                    onClick={() => onPodScan?.(it.id, `#${it.itemSeq.toString().padStart(2,"0")} ${it.itemId}`)}
                                    className="ml-auto rounded-full bg-[var(--color-success)] px-2.5 py-[3px] text-[10px] font-bold uppercase tracking-[0.06em] text-white transition-opacity hover:opacity-90"
                                  >
                                    Scan POD
                                  </button>
                                )}
                              </div>
                              {data.requiresDropPod === true && it.status === "DroppedOff" && it.droppedOffAt && (
                                <p className="mt-0.5 text-[10.5px] text-[var(--color-pastel-peach-ink)]">
                                  ⏱ Awaiting POD · dropped{" "}
                                  <DateTime value={it.droppedOffAt} variant="relative" />
                                </p>
                              )}
                              {it.description && (
                                <p className="mt-0.5 text-[12px] text-[var(--color-ink-500)] truncate">
                                  {it.description}
                                </p>
                              )}
                              <div className="mt-2 flex items-center gap-2 font-mono text-[11.5px]">
                                <span className="rounded-md bg-[var(--color-pastel-sky)] px-1.5 py-0.5 text-[var(--color-pastel-sky-ink)]">
                                  {it.pickupLocationCode}
                                </span>
                                <ArrowRight
                                  className="h-3 w-3 text-[var(--color-ink-400)]"
                                  strokeWidth={2.4}
                                />
                                <span className="rounded-md bg-[var(--color-pastel-mint)] px-1.5 py-0.5 text-[var(--color-pastel-mint-ink)]">
                                  {it.dropLocationCode}
                                </span>
                              </div>
                              {it.handlingInstructions.length > 0 && (
                                <div className="mt-2 flex flex-wrap gap-1">
                                  {it.handlingInstructions.map((h) => (
                                    <span
                                      key={h}
                                      className="rounded bg-[var(--color-amber-soft)] px-1.5 py-0.5 text-[10px] font-semibold uppercase tracking-[0.06em] text-[var(--color-amber)]"
                                    >
                                      {h}
                                    </span>
                                  ))}
                                </div>
                              )}
                            </div>
                            <div className="text-right text-[11.5px] font-mono tabular-nums text-[var(--color-ink-700)] shrink-0">
                              {it.quantity.value} {it.quantity.uom}
                              {it.weightKg != null && (
                                <div className="text-[var(--color-ink-400)]">
                                  {it.weightKg.toFixed(1)} kg
                                </div>
                              )}
                            </div>
                          </div>
                        </motion.div>
                      ))}
                    </div>
                  </section>
                </motion.div>
              )}
            </div>

            {/* Action footer */}
            {data && (
              <footer className="border-t border-[var(--color-ink-100)] px-6 py-4 dark:border-white/[0.06]">
                <div className="flex flex-wrap items-center justify-end gap-2">
                  {can(data.orderStatus, ["Draft"]) && (
                    <DrawerActionButton
                      tone="brand"
                      icon={<Send className="h-3.5 w-3.5" strokeWidth={2.4} />}
                      onClick={() => onAction("submit", data.id)}
                    >
                      Submit
                    </DrawerActionButton>
                  )}
                  {/* Phase P5 — the standalone Confirm button was retired.
                      Submit auto-confirms atomically, so the Submitted /
                      Validated states no longer occur durably. */}
                  {/* Cancel mirrors the backend guard: any non-terminal,
                      non-already-cancelled state. For in-flight states
                      (Dispatched / InProgress), the cancel cascades to
                      stop running trips at the vendor. For Failed, the
                      operator's choice is Cancel (terminal) vs Reopen
                      (recover and retry). */}
                  {!["Completed", "PartiallyCompleted", "Cancelled", "Rejected"]
                    .includes(data.orderStatus) && (
                    <DrawerActionButton
                      tone="coral"
                      icon={<Trash2 className="h-3.5 w-3.5" strokeWidth={2.4} />}
                      onClick={() => onAction("delete", data.id)}
                    >
                      Cancel order
                    </DrawerActionButton>
                  )}
                  {can(data.orderStatus, ["Failed", "Cancelled"]) && (
                    <DrawerActionButton
                      tone="lavender"
                      icon={<RotateCcw className="h-3.5 w-3.5" strokeWidth={2.4} />}
                      onClick={() => onAction("reopen", data.id)}
                    >
                      Reopen
                    </DrawerActionButton>
                  )}
                  {/* Reject — terminal alternative to Cancel, only valid
                      before the order is in-flight. After Confirmed →
                      Dispatched the order is already executing; use
                      Cancel (which cascades) instead. */}
                  {can(data.orderStatus, ["Submitted", "Validated", "Confirmed"]) && (
                    <DrawerActionButton
                      tone="coral"
                      icon={<Ban className="h-3.5 w-3.5" strokeWidth={2.4} />}
                      onClick={() => onAction("reject", data.id)}
                    >
                      Reject
                    </DrawerActionButton>
                  )}
                  {/* Hold — pause planning/dispatch on any live state.
                      Excludes terminal + already-held states. */}
                  {!["Held", "Completed", "PartiallyCompleted", "Cancelled", "Rejected", "Draft"]
                    .includes(data.orderStatus) && (
                    <DrawerActionButton
                      tone="amber"
                      icon={<PauseCircle className="h-3.5 w-3.5" strokeWidth={2.4} />}
                      onClick={() => onAction("hold", data.id)}
                    >
                      Hold
                    </DrawerActionButton>
                  )}
                  {/* Release — only from Held; re-fires planning. */}
                  {can(data.orderStatus, ["Held"]) && (
                    <DrawerActionButton
                      tone="success"
                      icon={<PlayCircle className="h-3.5 w-3.5" strokeWidth={2.4} />}
                      onClick={() => onAction("release", data.id)}
                    >
                      Release
                    </DrawerActionButton>
                  )}
                  {/* Redispatch: only meaningful when the order is
                      Confirmed but no Trip ever materialised. We can't
                      cheaply check the Trip count from this drawer's
                      data, so we show the button on Confirmed orders
                      and let the backend reject if active trips exist
                      (clear error message). Operators following the
                      Failed → Reopen → Redispatch path will land here
                      naturally. */}
                  {can(data.orderStatus, ["Confirmed"]) && (
                    <DrawerActionButton
                      tone="sky"
                      icon={<Send className="h-3.5 w-3.5" strokeWidth={2.4} />}
                      onClick={() => onAction("redispatch", data.id)}
                    >
                      Redispatch
                    </DrawerActionButton>
                  )}
                  {/* Phase b11 escape hatch — visible only when the order
                      is stranded at an in-flight status with no active
                      Trip remaining (every Trip terminal, typically all
                      Cancelled). New orders never need this — the
                      TripCancelledConsumer cascades automatically; this
                      button is for legacy pre-b11 rows + edge cases
                      where the cascade didn't fire. */}
                  {ORDER_IN_FLIGHT_STATES.includes(data.orderStatus) &&
                    trips !== null &&
                    trips.length > 0 &&
                    !trips.some((t) => ACTIVE_TRIP_STATES.includes(t.status)) && (
                      <DrawerActionButton
                        tone="amber"
                        icon={<Ban className="h-3.5 w-3.5" strokeWidth={2.4} />}
                        onClick={() => onAction("abandon", data.id)}
                      >
                        Abandon (no active trips)
                      </DrawerActionButton>
                    )}
                  {/* Reorder — always available. Source order is never
                      modified; a fresh draft is opened with items copied
                      over (orderRef + service window cleared). */}
                  <DrawerActionButton
                    tone="sky"
                    icon={<Copy className="h-3.5 w-3.5" strokeWidth={2.4} />}
                    onClick={() => onAction("reorder", data.id)}
                  >
                    Reorder
                  </DrawerActionButton>
                  <button
                    type="button"
                    onClick={onClose}
                    className="rounded-full bg-[var(--color-ink-100)] px-4 py-2 text-[12px] font-semibold text-[var(--color-ink-700)] transition-colors hover:bg-[var(--color-ink-200)] dark:bg-white/[0.06] dark:hover:bg-white/[0.1]"
                  >
                    Close
                  </button>
                </div>
              </footer>
            )}
          </motion.aside>
          {/* Stacked Trip drawer — sits above the order drawer, escape and
              backdrop close it without dismissing the order drawer. */}
          <TripDetailDrawer
            tripId={openTripId}
            onClose={() => setOpenTripId(null)}
            onOpenTrip={(id) => setOpenTripId(id)}
            onOpenOrder={(id) => {
              // Hop the order drawer to the clicked order. Close the
              // stacked trip drawer first so the user lands on the new
              // order's overview, not on a trip from the previous one.
              setOpenTripId(null);
              onOpenOrder?.(id);
            }}
          />
        </>
      )}
    </AnimatePresence>
  );
}

function MetaCell({
  icon,
  label,
  value,
}: {
  icon: React.ReactNode;
  label: string;
  value: React.ReactNode;
}) {
  return (
    <div className="rounded-xl bg-[var(--color-surface-soft)] px-3 py-2.5 dark:bg-white/[0.04]">
      <div className="flex items-center gap-1.5 text-[10px] font-semibold uppercase tracking-[0.12em] text-[var(--color-ink-400)]">
        {icon}
        {label}
      </div>
      <div className="mt-1 text-[12.5px] font-medium text-[var(--color-ink-900)] truncate">
        {value}
      </div>
    </div>
  );
}

function Totals({
  label,
  value,
  suffix,
  tone,
}: {
  label: string;
  value: number;
  suffix?: string;
  tone: "brand" | "amber" | "ink";
}) {
  const tones = {
    brand: "from-[var(--color-pastel-sky)] to-transparent text-[var(--color-pastel-sky-ink)]",
    amber:
      "from-[var(--color-amber-soft)] to-transparent text-[var(--color-amber)]",
    ink: "from-[var(--color-ink-100)] to-transparent text-[var(--color-ink-700)]",
  };
  return (
    <div
      className={cn(
        "rounded-xl bg-gradient-to-br p-3 dark:bg-white/[0.03]",
        tones[tone],
      )}
    >
      <div className="text-[10px] font-semibold uppercase tracking-[0.12em] opacity-80">
        {label}
      </div>
      <div className="mt-1 font-mono text-[1.2rem] font-semibold tabular-nums leading-none">
        {value.toLocaleString("en-US", { maximumFractionDigits: 1 })}
        {suffix && <span className="text-[10px] opacity-60 ml-0.5">{suffix}</span>}
      </div>
    </div>
  );
}

// Map free-text event types into a small palette. We pattern-match on
// substrings rather than the exact string because the backend mixes
// well-known names ("Created", "Submitted") with the synthetic
// "Amendment:<Type>" entries from the amendment repo.
function eventTone(eventType: string): { chip: string; dot: string } {
  const e = eventType.toLowerCase();
  if (e.includes("fail") || e.includes("reject") || e.includes("cancel"))
    return {
      chip: "bg-[var(--color-coral-soft)] text-[var(--color-coral)]",
      dot: "bg-[var(--color-coral)]",
    };
  if (e.includes("complete") || e.includes("delivered"))
    return {
      chip: "bg-[var(--color-success-soft)] text-[var(--color-success)]",
      dot: "bg-[var(--color-success)]",
    };
  if (e.includes("submit") || e.includes("validat") || e.includes("confirm"))
    return {
      chip: "bg-[var(--color-pastel-sky)] text-[var(--color-pastel-sky-ink)]",
      dot: "bg-[var(--color-brand-500)]",
    };
  if (e.includes("plan") || e.includes("dispatch") || e.includes("progress"))
    return {
      chip:
        "bg-[var(--color-pastel-lavender)] text-[var(--color-pastel-lavender-ink)]",
      dot: "bg-[var(--color-pastel-lavender-ink)]",
    };
  if (e.includes("amend") || e.includes("hold"))
    return {
      chip: "bg-[var(--color-amber-soft)] text-[var(--color-amber)]",
      dot: "bg-[var(--color-amber)]",
    };
  return {
    chip: "bg-[var(--color-ink-100)] text-[var(--color-ink-700)] dark:bg-white/[0.06]",
    dot: "bg-[var(--color-ink-400)]",
  };
}

function SectionLabel({ children }: { children: React.ReactNode }) {
  return (
    <h4 className="text-[10.5px] font-semibold uppercase tracking-[0.12em] text-[var(--color-ink-400)]">
      {children}
    </h4>
  );
}

function DrawerActionButton({
  tone,
  icon,
  onClick,
  children,
}: {
  tone: "brand" | "success" | "coral" | "lavender" | "sky" | "amber";
  icon: React.ReactNode;
  onClick: () => void;
  children: React.ReactNode;
}) {
  const tones = {
    brand:
      "bg-[var(--color-brand-900)] text-white hover:shadow-[0_14px_36px_-12px_rgba(15,23,42,0.6)] dark:bg-[var(--color-brand-500)] dark:text-[var(--color-ink-50)]",
    success:
      "bg-[var(--color-success)] text-white hover:shadow-[0_14px_36px_-12px_rgba(16,185,129,0.5)]",
    coral:
      "bg-[var(--color-coral-soft)] text-[var(--color-coral)] hover:bg-[var(--color-coral-soft)]/80",
    lavender:
      "bg-[var(--color-pastel-lavender)] text-[var(--color-pastel-lavender-ink)] hover:bg-[var(--color-pastel-lavender)]/80",
    sky:
      "bg-[var(--color-pastel-sky)] text-[var(--color-pastel-sky-ink)] hover:bg-[var(--color-pastel-sky)]/80",
    amber:
      "bg-[var(--color-amber-soft)] text-[var(--color-amber)] hover:bg-[var(--color-amber-soft)]/80",
  };
  return (
    <motion.button
      type="button"
      onClick={onClick}
      whileHover={{ y: -1 }}
      whileTap={{ scale: 0.97 }}
      className={cn(
        "inline-flex items-center gap-1.5 rounded-full px-4 py-2 text-[12px] font-semibold transition-all",
        tones[tone],
      )}
    >
      {icon}
      {children}
    </motion.button>
  );
}

// ── Requires-POD badge ─────────────────────────────────────────────────
// Surfaces the tri-state per-order POD policy in the drawer header so
// operators see at a glance whether the order will auto-deliver or sit
// at DroppedOff waiting for a scan. Null = "inherit from template".
function RequiresDropPodBadge({ value }: { value: boolean | null }) {
  if (value === true) {
    return (
      <span
        title="Items will sit at DroppedOff until /pod-scan is called"
        className="inline-flex items-center gap-1 rounded-full bg-[var(--color-pastel-peach)] px-2 py-[2px] text-[10px] font-semibold uppercase tracking-[0.06em] text-[var(--color-pastel-peach-ink)]"
      >
        POD required
      </span>
    );
  }
  if (value === false) {
    return (
      <span
        title="Items auto-deliver on TASK_FINISHED without a scan"
        className="inline-flex items-center gap-1 rounded-full bg-[var(--color-ink-100)] px-2 py-[2px] text-[10px] font-semibold uppercase tracking-[0.06em] text-[var(--color-ink-500)] dark:bg-white/[0.06]"
      >
        Auto-deliver
      </span>
    );
  }
  return (
    <span
      title="POD policy inherited from the route's OrderTemplate"
      className="inline-flex items-center gap-1 rounded-full bg-[var(--color-pastel-lavender)] px-2 py-[2px] text-[10px] font-semibold uppercase tracking-[0.06em] text-[var(--color-pastel-lavender-ink)]"
    >
      POD: inherit
    </span>
  );
}

// ── Item status badge ──────────────────────────────────────────────────
// Reflects the per-item lifecycle (Gap 5 + Picked addition):
//   Pending   = awaiting / in vendor queue
//   Picked    = robot finished pickup action, items in transit
//   Delivered = terminal success
//   Failed    = terminal failure (recoverable via Retry/Reopen)
//   Returned / Cancelled = reserved for non-envelope flows
function ItemStatusBadge({ status }: { status: ItemStatus }) {
  const visual = STATUS_VISUAL[status] ?? STATUS_VISUAL.Pending;
  return (
    <span
      className={cn(
        "inline-flex items-center gap-1 rounded-md px-1.5 py-[2px] text-[9.5px] font-bold uppercase tracking-[0.06em]",
        visual.className,
      )}
      title={visual.title}
    >
      <span className={cn("h-1.5 w-1.5 rounded-full", visual.dot)} />
      {status}
    </span>
  );
}

const STATUS_VISUAL: Record<
  ItemStatus,
  { className: string; dot: string; title: string }
> = {
  Pending: {
    className: "bg-[var(--color-ink-100)] text-[var(--color-ink-700)] dark:bg-white/[0.06]",
    dot: "bg-[var(--color-ink-400)]",
    title: "Awaiting dispatch or in vendor queue",
  },
  Picked: {
    className: "bg-[var(--color-amber-soft)] text-[var(--color-amber)]",
    dot: "bg-[var(--color-amber)]",
    title: "Robot picked up — in transit to drop station",
  },
  DroppedOff: {
    className: "bg-[var(--color-pastel-peach)] text-[var(--color-pastel-peach-ink)]",
    dot: "bg-[var(--color-pastel-peach-ink)]",
    title: "At drop dock — POD pending",
  },
  Delivered: {
    className: "bg-[var(--color-success-soft)] text-[var(--color-success)]",
    dot: "bg-[var(--color-success)]",
    title: "Delivered to drop station",
  },
  Failed: {
    className: "bg-[var(--color-coral-soft)] text-[var(--color-coral)]",
    dot: "bg-[var(--color-coral)]",
    title: "Dispatch or delivery failed — Reopen + Retry to recover",
  },
  Returned: {
    className: "bg-[var(--color-pastel-lavender)] text-[var(--color-pastel-lavender-ink)]",
    dot: "bg-[var(--color-pastel-lavender-ink)]",
    title: "Returned by vendor",
  },
  Cancelled: {
    className: "bg-[var(--color-ink-100)] text-[var(--color-ink-500)] dark:bg-white/[0.06]",
    dot: "bg-[var(--color-ink-400)]",
    title: "Cancelled by admin",
  },
};
