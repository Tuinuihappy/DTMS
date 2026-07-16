"use client";

import { RotateCcw, X } from "lucide-react";
import { AnimatePresence, motion } from "motion/react";
import { useEffect, useState } from "react";
import { cn } from "@/lib/utils";

// Modal for the admin "Reopen order" action (Failed or Cancelled).
// Required fields: reopenedBy (current user id) + reason (free text).
// On success the caller refreshes the order; the operator then triggers
// /retry on the failed/cancelled Trip separately — the two-step audit
// trail distinguishes "who reopened" from "who retried". Reopening a
// Cancelled order also reinstates cascade-cancelled items server-side.
export function ReopenDialog({
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
  onConfirm: (input: { reopenedBy: string; reason: string; autoRetry: boolean }) => Promise<void> | void;
  busy?: boolean;
  error?: string | null;
}) {
  const [reason, setReason] = useState("");
  const [reopenedBy, setReopenedBy] = useState(currentUser ?? "");
  const [autoRetry, setAutoRetry] = useState(true);

  useEffect(() => {
    if (open) {
      setReason("");
      setReopenedBy(currentUser ?? "");
      setAutoRetry(true);
    }
  }, [open, currentUser]);

  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => e.key === "Escape" && onClose();
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [open, onClose]);

  const canSubmit = reason.trim().length > 0 && reopenedBy.trim().length > 0 && !busy;

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
                <span className="inline-flex h-9 w-9 items-center justify-center rounded-full bg-[var(--color-pastel-lavender)] text-[var(--color-pastel-lavender-ink)]">
                  <RotateCcw className="h-4 w-4" strokeWidth={2.4} />
                </span>
                <div>
                  <h2 className="text-[15px] font-semibold text-[var(--color-ink-900)]">
                    Reopen order
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
                await onConfirm({ reopenedBy: reopenedBy.trim(), reason: reason.trim(), autoRetry });
              }}
              className="space-y-4 px-5 py-4"
            >
              <p className="rounded-lg bg-[var(--color-pastel-lavender)]/40 px-3 py-2.5 text-[12px] leading-relaxed text-[var(--color-pastel-lavender-ink)]">
                Reopen sets the order back to <strong>Confirmed</strong>
                {autoRetry ? (
                  <> and immediately retries its failed/cancelled trip(s) — the audit log
                  still records the reopen and each retry separately.</>
                ) : (
                  <>. After this, click <strong>Retry</strong> on the failed or cancelled
                  Trip to redispatch — the audit log keeps the reopen and retry events
                  separate.</>
                )}
              </p>

              <label className="flex cursor-pointer items-center gap-2.5 rounded-lg border border-[var(--color-ink-100)] px-3 py-2.5 dark:border-white/[0.06]">
                <input
                  type="checkbox"
                  checked={autoRetry}
                  onChange={(e) => setAutoRetry(e.target.checked)}
                  className="h-4 w-4 rounded accent-[var(--color-brand-500)]"
                />
                <span className="text-[12.5px] font-medium text-[var(--color-ink-900)]">
                  Retry trips immediately
                  <span className="block text-[11px] font-normal text-[var(--color-ink-500)]">
                    Uncheck if you need to fix something (station, template) before redispatching.
                  </span>
                </span>
              </label>

              <label className="block">
                <span className="text-[10.5px] font-semibold uppercase tracking-[0.06em] text-[var(--color-ink-500)]">
                  Reopened by
                </span>
                <input
                  type="text"
                  value={reopenedBy}
                  onChange={(e) => setReopenedBy(e.target.value)}
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
                  placeholder="e.g. vendor confirmed obstacle cleared; ready to retry"
                  rows={3}
                  className="mt-1 w-full resize-none rounded-lg border border-[var(--color-ink-100)] bg-[var(--color-surface)] px-3 py-2 text-[13px] text-[var(--color-ink-900)] focus:border-[var(--color-brand-500)] focus:outline-none focus:ring-2 focus:ring-[var(--color-brand-500)]/20 dark:border-white/[0.06] dark:bg-white/[0.02]"
                  required
                />
              </label>

              {error && (
                <div className="rounded-lg bg-[var(--color-coral-soft)] px-3 py-2 text-[11.5px] font-medium text-[var(--color-coral)]">
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
                      ? "bg-[var(--color-pastel-lavender)] text-[var(--color-pastel-lavender-ink)] hover:bg-[var(--color-pastel-lavender)]/80"
                      : "cursor-not-allowed bg-[var(--color-ink-100)] text-[var(--color-ink-400)] dark:bg-white/[0.04]",
                  )}
                >
                  <RotateCcw
                    className={cn("h-3.5 w-3.5", busy && "animate-spin")}
                    strokeWidth={2.4}
                  />
                  Reopen
                </button>
              </div>
            </form>
          </motion.div>
        </>
      )}
    </AnimatePresence>
  );
}
