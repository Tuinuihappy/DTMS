"use client";

import {
  ArrowRight,
  Box,
  Calendar,
  Check,
  ChevronRight,
  Clock,
  Hash,
  History,
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
import { AttemptBadge, TripStatusBadge } from "@/components/dispatch/badges";
import { TripDetailDrawer } from "@/components/dispatch/trip-detail-drawer";
import { FullAuditLog } from "./full-audit-log";
import { cn } from "@/lib/utils";
import { PriorityBadge, StatusBadge, TransportModeBadge } from "./badges";

type Action = "submit" | "confirm" | "delete" | "reopen" | "redispatch";

export function OrderDetailDrawer({
  orderId,
  onClose,
  onAction,
  onPodScan,
}: {
  orderId: string | null;
  onClose: () => void;
  onAction: (a: Action, id: string) => Promise<void> | void;
  onPodScan?: (itemId: string, itemLabel: string) => void;
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
  // Currently-open Trip detail drawer (stacks above this drawer).
  const [openTripId, setOpenTripId] = useState<string | null>(null);

  useEffect(() => {
    if (!orderId) {
      setData(null);
      setTimeline(null);
      setTrips(null);
      setError(null);
      return;
    }
    let cancelled = false;
    setLoading(true);
    setError(null);
    setTimeline(null);
    setTrips(null);

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
                <div className="rounded-xl bg-[#fde0db] px-4 py-3 text-[12.5px] font-medium text-[var(--color-coral)] dark:bg-[#3a1a17]">
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
                      value={data.sourceSystem}
                    />
                    <MetaCell
                      icon={<User className="h-3 w-3" strokeWidth={2.2} />}
                      label="Requested by"
                      value={data.requestedBy ?? "—"}
                    />
                    <MetaCell
                      icon={<Calendar className="h-3 w-3" strokeWidth={2.2} />}
                      label="Created"
                      value={new Date(data.createdDate).toLocaleString()}
                    />
                    <MetaCell
                      icon={<Clock className="h-3 w-3" strokeWidth={2.2} />}
                      label="Window"
                      value={
                        data.serviceWindow?.latestUtc
                          ? new Date(data.serviceWindow.latestUtc).toLocaleString()
                          : "—"
                      }
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

                  {/* Notes */}
                  {data.notes && (
                    <section>
                      <SectionLabel>Notes</SectionLabel>
                      <div className="mt-2 rounded-xl bg-[var(--color-surface-soft)] px-4 py-3 text-[13px] leading-relaxed text-[var(--color-ink-700)] dark:bg-white/[0.04]">
                        {data.notes}
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
                              <div className="flex items-center gap-2">
                                <TripStatusBadge status={t.status as never} />
                                <AttemptBadge attempt={t.attemptNumber} />
                                {t.vendorOrderKey && (
                                  <span className="font-mono text-[11.5px] font-semibold text-[var(--color-ink-700)]">
                                    #{t.vendorOrderKey}
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
                                {it.status === "DroppedOff" && (
                                  <button
                                    type="button"
                                    onClick={() => onPodScan?.(it.id, `#${it.itemSeq.toString().padStart(2,"0")} ${it.itemId}`)}
                                    className="ml-auto rounded-full bg-[var(--color-success)] px-2.5 py-[3px] text-[10px] font-bold uppercase tracking-[0.06em] text-white transition-opacity hover:opacity-90"
                                  >
                                    Scan POD
                                  </button>
                                )}
                                {it.status === "Delivered" && it.podScannedBy && (
                                  <span className="ml-auto inline-flex items-center gap-1 rounded-full bg-[var(--color-success-soft)] px-2 py-[2px] text-[9.5px] font-semibold uppercase tracking-[0.06em] text-[var(--color-success)]">
                                    POD · {it.podMethod}
                                  </span>
                                )}
                              </div>
                              {it.status === "DroppedOff" && it.droppedOffAt && (
                                <p className="mt-0.5 text-[10.5px] text-[var(--color-pastel-peach-ink)]">
                                  ⏱ Awaiting POD · dropped {relativeFromNow(it.droppedOffAt)}
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
                  {can(data.orderStatus, ["Submitted", "Validated"]) && (
                    <DrawerActionButton
                      tone="success"
                      icon={<Check className="h-3.5 w-3.5" strokeWidth={2.4} />}
                      onClick={() => onAction("confirm", data.id)}
                    >
                      Confirm
                    </DrawerActionButton>
                  )}
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
                  {can(data.orderStatus, ["Failed"]) && (
                    <DrawerActionButton
                      tone="lavender"
                      icon={<RotateCcw className="h-3.5 w-3.5" strokeWidth={2.4} />}
                      onClick={() => onAction("reopen", data.id)}
                    >
                      Reopen
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
  value: string;
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
      chip: "bg-[#fde0db] text-[var(--color-coral)] dark:bg-[#3a1a17]",
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

function relativeFromNow(iso: string): string {
  const d = new Date(iso);
  if (isNaN(d.getTime())) return "—";
  const diff = Date.now() - d.getTime();
  const min = Math.floor(diff / 60_000);
  if (min < 1) return "just now";
  if (min < 60) return `${min}m ago`;
  const hr = Math.floor(min / 60);
  if (hr < 24) return `${hr}h ago`;
  const day = Math.floor(hr / 24);
  if (day < 7) return `${day}d ago`;
  return d.toLocaleDateString();
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
  tone: "brand" | "success" | "coral" | "lavender" | "sky";
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
      "bg-[#fde0db] text-[var(--color-coral)] hover:bg-[#fbc7be] dark:bg-[#3a1a17] dark:hover:bg-[#4a2520]",
    lavender:
      "bg-[var(--color-pastel-lavender)] text-[var(--color-pastel-lavender-ink)] hover:bg-[var(--color-pastel-lavender)]/80",
    sky:
      "bg-[var(--color-pastel-sky)] text-[var(--color-pastel-sky-ink)] hover:bg-[var(--color-pastel-sky)]/80",
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
    className: "bg-[#fde0db] text-[var(--color-coral)] dark:bg-[#3a1a17]",
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
