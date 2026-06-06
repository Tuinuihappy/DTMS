"use client";

import { Send, X } from "lucide-react";
import { AnimatePresence, motion } from "motion/react";
import { useEffect, useState } from "react";
import { cn } from "@/lib/utils";

/**
 * Modal for the operator "Redispatch order" action. Used when an order
 * never produced a Trip (every group failed vendor dispatch) and the
 * operator has reopened it back to Confirmed. The backend re-fires the
 * Confirmed integration event so Planning's consumer runs the dispatch
 * loop again.
 */
export function RedispatchDialog({
  orderRef,
  currentUser,
  open,
  onClose,
  onConfirm,
  busy,
  error,
}: {
  orderRef: string | null;
  currentUser: string | null;
  open: boolean;
  onClose: () => void;
  onConfirm: (input: { redispatchedBy: string; reason: string }) => Promise<void> | void;
  busy?: boolean;
  error?: string | null;
}) {
  const [reason, setReason] = useState("");
  const [redispatchedBy, setRedispatchedBy] = useState(currentUser ?? "");

  useEffect(() => {
    if (open) {
      setReason("");
      setRedispatchedBy(currentUser ?? "");
    }
  }, [open, currentUser]);

  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => e.key === "Escape" && onClose();
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [open, onClose]);

  const canSubmit = reason.trim().length > 0 && redispatchedBy.trim().length > 0 && !busy;

  return (
    <AnimatePresence>
      {open && (
        <>
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            transition={{ duration: 0.2 }}
            onClick={onClose}
            className="fixed inset-0 z-[80] bg-[var(--color-ink-900)]/50 backdrop-blur-sm"
          />
          <motion.div
            initial={{ opacity: 0, scale: 0.96, y: 8 }}
            animate={{ opacity: 1, scale: 1, y: 0 }}
            exit={{ opacity: 0, scale: 0.96 }}
            transition={{ duration: 0.22 }}
            className={cn(
              "fixed left-1/2 top-1/2 z-[90] w-[min(480px,calc(100vw-32px))] -translate-x-1/2 -translate-y-1/2",
              "rounded-2xl bg-[var(--color-surface)] shadow-[0_30px_80px_-20px_rgba(15,23,42,0.5)] dark:bg-[var(--color-surface)]",
            )}
            role="dialog"
            aria-modal="true"
          >
            <header className="flex items-start justify-between gap-3 border-b border-[var(--color-ink-100)] px-5 py-4 dark:border-white/[0.06]">
              <div className="flex items-center gap-2.5">
                <span className="inline-flex h-9 w-9 items-center justify-center rounded-full bg-[var(--color-pastel-sky)] text-[var(--color-pastel-sky-ink)]">
                  <Send className="h-4 w-4" strokeWidth={2.4} />
                </span>
                <div>
                  <h2 className="text-[15px] font-semibold text-[var(--color-ink-900)]">
                    Redispatch order
                  </h2>
                  {orderRef && (
                    <p className="mt-0.5 font-mono text-[11px] text-[var(--color-ink-500)]">
                      {orderRef}
                    </p>
                  )}
                </div>
              </div>
              <button
                type="button"
                onClick={onClose}
                className="rounded-full p-1.5 text-[var(--color-ink-500)] transition-colors hover:bg-[var(--color-ink-100)] dark:hover:bg-white/10"
                aria-label="Close"
              >
                <X className="h-4 w-4" strokeWidth={2.4} />
              </button>
            </header>

            <form
              onSubmit={async (e) => {
                e.preventDefault();
                if (!canSubmit) return;
                await onConfirm({ redispatchedBy: redispatchedBy.trim(), reason: reason.trim() });
              }}
              className="space-y-4 px-5 py-4"
            >
              <p className="rounded-lg bg-[var(--color-pastel-sky)]/40 px-3 py-2.5 text-[12px] leading-relaxed text-[var(--color-pastel-sky-ink)]">
                Re-fires the Planning dispatch loop. Use this when the order
                never produced a Trip (e.g. no <code>OrderTemplate</code> existed for the
                route). After redispatch, the Planning consumer groups items
                and tries each route again.
              </p>

              <label className="block">
                <span className="text-[10.5px] font-semibold uppercase tracking-[0.06em] text-[var(--color-ink-500)]">
                  Redispatched by
                </span>
                <input
                  type="text"
                  value={redispatchedBy}
                  onChange={(e) => setRedispatchedBy(e.target.value)}
                  placeholder="e.g. ops-lead-01"
                  className="mt-1 w-full rounded-lg border border-[var(--color-ink-100)] bg-[var(--color-surface)] px-3 py-2 text-[13px] text-[var(--color-ink-900)] focus:border-[var(--color-brand-500)] focus:outline-none focus:ring-2 focus:ring-[var(--color-brand-500)]/20 dark:border-white/[0.06] dark:bg-white/[0.02]"
                  required
                />
              </label>

              <label className="block">
                <span className="text-[10.5px] font-semibold uppercase tracking-[0.06em] text-[var(--color-ink-500)]">
                  Reason
                </span>
                <textarea
                  value={reason}
                  onChange={(e) => setReason(e.target.value)}
                  placeholder="e.g. template now registered for STF_37 → F-19"
                  rows={3}
                  className="mt-1 w-full resize-none rounded-lg border border-[var(--color-ink-100)] bg-[var(--color-surface)] px-3 py-2 text-[13px] text-[var(--color-ink-900)] focus:border-[var(--color-brand-500)] focus:outline-none focus:ring-2 focus:ring-[var(--color-brand-500)]/20 dark:border-white/[0.06] dark:bg-white/[0.02]"
                  required
                />
              </label>

              {error && (
                <div className="rounded-lg bg-[#fde0db] px-3 py-2 text-[11.5px] font-medium text-[var(--color-coral)] dark:bg-[#3a1a17]">
                  {error}
                </div>
              )}

              <div className="flex items-center justify-end gap-2 pt-1">
                <button
                  type="button"
                  onClick={onClose}
                  className="rounded-full px-4 py-2 text-[12px] font-semibold text-[var(--color-ink-700)] transition-colors hover:bg-[var(--color-ink-100)] dark:text-[var(--color-ink-500)] dark:hover:bg-white/10"
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  disabled={!canSubmit}
                  className={cn(
                    "inline-flex items-center gap-1.5 rounded-full px-4 py-2 text-[12px] font-semibold uppercase tracking-[0.06em] transition-all",
                    canSubmit
                      ? "bg-[var(--color-pastel-sky)] text-[var(--color-pastel-sky-ink)] hover:bg-[var(--color-pastel-sky)]/80"
                      : "cursor-not-allowed bg-[var(--color-ink-100)] text-[var(--color-ink-400)] dark:bg-white/[0.04]",
                  )}
                >
                  <Send
                    className={cn("h-3.5 w-3.5", busy && "animate-pulse")}
                    strokeWidth={2.4}
                  />
                  Redispatch
                </button>
              </div>
            </form>
          </motion.div>
        </>
      )}
    </AnimatePresence>
  );
}
