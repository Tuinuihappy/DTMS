"use client";

import { AlertTriangle, Bot, Loader2, Trash2, X } from "lucide-react";
import { AnimatePresence, motion } from "motion/react";
import { OverlayBackdrop } from "@/components/primitives/overlay-backdrop";
import { useEffect, useState } from "react";
import { cn } from "@/lib/utils";

export function CancelOrderDialog({
  open,
  orderRef,
  count = 1,
  busy,
  // Number of in-flight Trips the cascade will stop (Created /
  // InProgress / Paused). Surfaces as a callout so the operator knows
  // robots will be told to stop, not just bookkeeping.
  activeTripCount = 0,
  onClose,
  onConfirm,
}: {
  open: boolean;
  orderRef: string | null;
  // When > 1, the dialog renders as a bulk-cancel ("Cancel 5 orders")
  // and the orderRef is interpreted as a roll-up label ("5 orders") or
  // ignored. The 6s undo grace period still applies per-order.
  count?: number;
  busy: boolean;
  activeTripCount?: number;
  onClose: () => void;
  onConfirm: (reason: string) => void;
}) {
  const [reason, setReason] = useState("");

  useEffect(() => {
    if (open) setReason("");
  }, [open]);

  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => e.key === "Escape" && !busy && onClose();
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [open, onClose, busy]);

  const canSubmit = reason.trim().length >= 3 && !busy;

  return (
    <>
      {/* State-driven backdrop — see OverlayBackdrop for the stuck-exit
          rationale. Wrapper is pointer-events-none (panel re-enables)
          so a stranded exit can never swallow page clicks. */}
      <OverlayBackdrop
        open={open}
        onClick={() => !busy && onClose()}
        className="z-40 bg-[var(--color-ink-900)]/55 backdrop-blur-md"
      />
      <AnimatePresence>
        {open && (
          <div
            key="cancel-order-dialog"
            className="pointer-events-none fixed inset-0 z-50 flex items-center justify-center p-4"
          >
            <motion.div
              initial={{ opacity: 0, scale: 0.94, y: 12 }}
              animate={{ opacity: 1, scale: 1, y: 0 }}
              exit={{ opacity: 0, scale: 0.96, y: 12, transition: { duration: 0.16 } }}
              transition={{ type: "spring", stiffness: 360, damping: 30 }}
              className="pointer-events-auto relative w-full max-w-md overflow-hidden rounded-[var(--radius-xl)] glass-strong"
            >
              <header className="flex items-start gap-3 px-6 pt-5">
                <span className="grid h-10 w-10 shrink-0 place-items-center rounded-full bg-[var(--color-coral-soft)] text-[var(--color-coral)]">
                  <AlertTriangle className="h-5 w-5" strokeWidth={2.2} />
                </span>
                <div className="min-w-0 flex-1">
                  <div className="text-[10.5px] font-semibold uppercase tracking-[0.12em] text-[var(--color-ink-400)]">
                    {count > 1 ? `Cancel ${count} orders` : "Cancel order"}
                  </div>
                  <h2 className="font-display mt-1 text-[1.2rem] font-semibold text-[var(--color-ink-900)] truncate">
                    {count > 1 ? `${count} selected` : (orderRef ?? "—")}
                  </h2>
                </div>
                <button
                  type="button"
                  onClick={() => !busy && onClose()}
                  className="rounded-full p-2 text-[var(--color-ink-500)] transition-colors hover:bg-white/40 hover:text-[var(--color-ink-900)] dark:hover:bg-white/10"
                  aria-label="Close"
                >
                  <X className="h-4 w-4" strokeWidth={2.4} />
                </button>
              </header>

              <div className="px-6 pb-2 pt-3">
                <p className="text-[13px] leading-relaxed text-[var(--color-ink-600)]">
                  {count > 1 ? (
                    <>
                      This will move <span className="font-semibold">{count} orders</span> to{" "}
                      <span className="font-semibold">Cancelled</span> with the same reason.
                      You can still undo each one individually within 6 seconds.
                    </>
                  ) : (
                    <>
                      This will move the order to{" "}
                      <span className="font-semibold">Cancelled</span> and notify downstream
                      services. Please tell the team why.
                    </>
                  )}
                </p>
                {activeTripCount > 0 && count <= 1 && (
                  <div className="mt-3 flex items-start gap-2 rounded-lg bg-[var(--color-coral-soft)]/60 px-3 py-2 text-[11.5px] leading-relaxed text-[var(--color-coral)]">
                    <Bot className="mt-0.5 h-3.5 w-3.5 flex-shrink-0" strokeWidth={2.4} />
                    <span>
                      <strong>Cascade:</strong> {activeTripCount} in-flight trip
                      {activeTripCount === 1 ? "" : "s"} will be cancelled at RIOT3 — robot
                      {activeTripCount === 1 ? "" : "s"} will stop and return to base.
                    </span>
                  </div>
                )}
              </div>

              <div className="px-6 pb-4">
                <label className="block">
                  <span className="block text-[10.5px] font-semibold uppercase tracking-[0.1em] text-[var(--color-ink-500)] mb-1.5">
                    Reason <span className="text-[var(--color-coral)]">*</span>
                  </span>
                  <textarea
                    value={reason}
                    onChange={(e) => setReason(e.target.value)}
                    rows={3}
                    placeholder="e.g. duplicate request, requester withdrew, route blocked…"
                    autoFocus
                    className={cn(
                      "w-full rounded-lg bg-white/70 px-3 py-2 text-[13px] font-medium resize-none",
                      "border border-white/80 backdrop-blur-md transition-all",
                      "placeholder:text-[var(--color-ink-400)] text-[var(--color-ink-900)]",
                      "focus:outline-none focus:ring-2 focus:ring-[var(--color-coral)]/40 focus:border-[var(--color-coral)]/30",
                      "dark:bg-white/[0.05] dark:border-white/10",
                    )}
                  />
                </label>
                <p className="mt-1 text-[10.5px] text-[var(--color-ink-400)]">
                  At least 3 characters · stored in the audit log
                </p>
              </div>

              <footer className="flex items-center justify-end gap-2 border-t border-white/40 px-6 py-4 dark:border-white/[0.06]">
                <button
                  type="button"
                  onClick={() => !busy && onClose()}
                  className="rounded-full bg-white/40 px-4 py-2 text-[12px] font-semibold text-[var(--color-ink-700)] transition-colors hover:bg-white/70 dark:bg-white/[0.05] dark:hover:bg-white/[0.1]"
                >
                  Back
                </button>
                <motion.button
                  type="button"
                  onClick={() => canSubmit && onConfirm(reason.trim())}
                  disabled={!canSubmit}
                  whileHover={canSubmit ? { y: -1 } : {}}
                  whileTap={canSubmit ? { scale: 0.97 } : {}}
                  className={cn(
                    "inline-flex items-center gap-1.5 rounded-full px-4 py-2 text-[12px] font-semibold transition-all",
                    canSubmit
                      ? "bg-[var(--color-coral)] text-white hover:shadow-[0_14px_36px_-12px_rgba(255,107,91,0.6)]"
                      : "bg-[var(--color-ink-100)] text-[var(--color-ink-400)] cursor-not-allowed dark:bg-white/[0.04]",
                  )}
                >
                  {busy ? (
                    <Loader2 className="h-3.5 w-3.5 animate-spin" strokeWidth={2.4} />
                  ) : (
                    <Trash2 className="h-3.5 w-3.5" strokeWidth={2.4} />
                  )}
                  {busy ? "Cancelling…" : "Confirm cancel"}
                </motion.button>
              </footer>
            </motion.div>
          </div>
        )}
      </AnimatePresence>
    </>
  );
}
