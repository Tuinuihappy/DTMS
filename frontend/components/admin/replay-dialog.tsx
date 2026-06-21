"use client";

import { AlertTriangle, Loader2, RotateCcw, X } from "lucide-react";
import { AnimatePresence, motion } from "motion/react";
import { useEffect, useState } from "react";
import {
  replayProjector,
  type ReplaySummary,
} from "@/lib/api/admin-projections";
import { cn } from "@/lib/utils";
import {
  fromDateTimeLocalInput,
  toDateTimeLocalInput,
} from "@/lib/datetime";

// P0 Day 6 — modal that collects replay parameters + confirms before
// firing. Reused by every "Replay" button on the projections health
// page. Two-step UX (form → confirm) so an accidental click on a
// running production projector can't trigger an immediate rebuild.

export function ReplayDialog({
  open,
  projectorName,
  onClose,
  onReplayed,
}: {
  open: boolean;
  projectorName: string | null;
  onClose: () => void;
  /** Called after a successful replay completes. */
  onReplayed?: (summary: ReplaySummary) => void;
}) {
  // Default window — last 24h of events. Chosen so the most common
  // "fix a bug in the projector + rebuild yesterday's data" case is
  // one form submission away.
  const [fromUtc, setFromUtc] = useState("");
  const [toUtc, setToUtc] = useState("");
  const [aggregateId, setAggregateId] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [confirmStage, setConfirmStage] = useState<"form" | "confirm">("form");

  useEffect(() => {
    if (!open) return;
    const now = new Date();
    const yesterday = new Date(now.getTime() - 24 * 60 * 60 * 1000);
    setFromUtc(toDateTimeLocalInput(yesterday));
    setToUtc(toDateTimeLocalInput(now));
    setAggregateId("");
    setError(null);
    setSubmitting(false);
    setConfirmStage("form");
  }, [open]);

  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => e.key === "Escape" && !submitting && onClose();
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [open, onClose, submitting]);

  const submit = async () => {
    if (!projectorName) return;
    const fromIso = fromDateTimeLocalInput(fromUtc);
    if (!fromIso) {
      setError("FromUtc is required.");
      return;
    }
    setSubmitting(true);
    setError(null);
    try {
      const result = await replayProjector(projectorName, {
        fromUtc: fromIso,
        toUtc: fromDateTimeLocalInput(toUtc) ?? undefined,
        aggregateId: aggregateId.trim() || undefined,
      });
      if (result.ok) {
        onReplayed?.(result.summary);
        onClose();
      } else {
        setError(result.message);
        // Drop back to the form so the operator can adjust + retry.
        setConfirmStage("form");
      }
    } catch (e) {
      setError((e as Error).message);
      setConfirmStage("form");
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <AnimatePresence>
      {open && projectorName && (
        <>
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            transition={{ duration: 0.2 }}
            onClick={!submitting ? onClose : undefined}
            className="fixed inset-0 z-[80] bg-[var(--color-ink-900)]/50 backdrop-blur-sm"
          />
          <motion.div
            initial={{ opacity: 0, scale: 0.96, y: 8 }}
            animate={{ opacity: 1, scale: 1, y: 0 }}
            exit={{ opacity: 0, scale: 0.96 }}
            transition={{ duration: 0.22 }}
            role="dialog"
            aria-modal="true"
            className={cn(
              "fixed left-1/2 top-1/2 z-[90] w-[min(500px,calc(100vw-32px))] -translate-x-1/2 -translate-y-1/2",
              "rounded-2xl bg-[var(--color-surface)] shadow-[0_30px_80px_-20px_rgba(15,23,42,0.5)] dark:bg-[var(--color-surface)]",
            )}
          >
            <header className="flex items-start justify-between gap-3 border-b border-[var(--color-ink-100)] px-5 py-4 dark:border-white/[0.06]">
              <div className="flex items-center gap-2.5">
                <span className="inline-flex h-9 w-9 items-center justify-center rounded-full bg-[var(--color-pastel-lavender)] text-[var(--color-pastel-lavender-ink)]">
                  <RotateCcw className="h-4 w-4" strokeWidth={2.4} />
                </span>
                <div>
                  <h2 className="text-[15px] font-semibold text-[var(--color-ink-900)]">
                    Replay projector
                  </h2>
                  <p className="mt-0.5 font-mono text-[11px] text-[var(--color-ink-500)]">
                    {projectorName}
                  </p>
                </div>
              </div>
              <button
                type="button"
                onClick={onClose}
                disabled={submitting}
                className="rounded-full p-1.5 text-[var(--color-ink-500)] hover:bg-[var(--color-ink-100)] disabled:opacity-50 dark:hover:bg-white/10"
                aria-label="Close"
              >
                <X className="h-4 w-4" strokeWidth={2.4} />
              </button>
            </header>

            {confirmStage === "form" ? (
              <form
                onSubmit={(e) => {
                  e.preventDefault();
                  if (!fromUtc) {
                    setError("FromUtc is required.");
                    return;
                  }
                  setError(null);
                  setConfirmStage("confirm");
                }}
                className="space-y-4 px-5 py-4"
              >
                <p className="rounded-lg bg-[var(--color-pastel-lavender)]/40 px-3 py-2.5 text-[12px] leading-relaxed text-[var(--color-pastel-lavender-ink)]">
                  Replay re-feeds historical events through the projector to
                  rebuild its read model. Existing rows the projector wrote
                  are overwritten via the inbox idempotency check.
                </p>

                <label className="block">
                  <span className="text-[10.5px] font-semibold uppercase tracking-[0.06em] text-[var(--color-ink-500)]">
                    From (UTC)
                  </span>
                  <input
                    type="datetime-local"
                    value={fromUtc}
                    onChange={(e) => setFromUtc(e.target.value)}
                    required
                    className="mt-1 w-full rounded-lg border border-[var(--color-ink-100)] bg-[var(--color-surface)] px-3 py-2 text-[13px] text-[var(--color-ink-900)] focus:border-[var(--color-brand-500)] focus:outline-none focus:ring-2 focus:ring-[var(--color-brand-500)]/20 dark:border-white/[0.06] dark:bg-white/[0.02]"
                  />
                </label>

                <label className="block">
                  <span className="text-[10.5px] font-semibold uppercase tracking-[0.06em] text-[var(--color-ink-500)]">
                    To (UTC)
                    <span className="ml-1 normal-case tracking-normal text-[var(--color-ink-400)]">
                      · optional, defaults to now
                    </span>
                  </span>
                  <input
                    type="datetime-local"
                    value={toUtc}
                    onChange={(e) => setToUtc(e.target.value)}
                    className="mt-1 w-full rounded-lg border border-[var(--color-ink-100)] bg-[var(--color-surface)] px-3 py-2 text-[13px] text-[var(--color-ink-900)] focus:border-[var(--color-brand-500)] focus:outline-none focus:ring-2 focus:ring-[var(--color-brand-500)]/20 dark:border-white/[0.06] dark:bg-white/[0.02]"
                  />
                </label>

                <label className="block">
                  <span className="text-[10.5px] font-semibold uppercase tracking-[0.06em] text-[var(--color-ink-500)]">
                    Aggregate id
                    <span className="ml-1 normal-case tracking-normal text-[var(--color-ink-400)]">
                      · optional, leave blank to replay all
                    </span>
                  </span>
                  <input
                    type="text"
                    value={aggregateId}
                    onChange={(e) => setAggregateId(e.target.value)}
                    placeholder="e.g. 7f3a9c12-…"
                    className="mt-1 w-full rounded-lg border border-[var(--color-ink-100)] bg-[var(--color-surface)] px-3 py-2 font-mono text-[12px] text-[var(--color-ink-900)] focus:border-[var(--color-brand-500)] focus:outline-none focus:ring-2 focus:ring-[var(--color-brand-500)]/20 dark:border-white/[0.06] dark:bg-white/[0.02]"
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
                    className="rounded-full px-4 py-2 text-[12px] font-semibold text-[var(--color-ink-700)] hover:bg-[var(--color-ink-100)] dark:text-[var(--color-ink-500)] dark:hover:bg-white/10"
                  >
                    Cancel
                  </button>
                  <button
                    type="submit"
                    className="inline-flex items-center gap-1.5 rounded-full bg-[var(--color-pastel-lavender)] px-4 py-2 text-[12px] font-semibold uppercase tracking-[0.06em] text-[var(--color-pastel-lavender-ink)] hover:bg-[var(--color-pastel-lavender)]/80"
                  >
                    Review
                  </button>
                </div>
              </form>
            ) : (
              <div className="space-y-4 px-5 py-4">
                <div className="rounded-lg border border-[var(--color-amber)]/40 bg-[var(--color-amber-soft)] px-3 py-2.5 text-[12px] leading-relaxed text-[var(--color-amber)]">
                  <div className="flex items-start gap-2">
                    <AlertTriangle className="mt-[2px] h-4 w-4 shrink-0" strokeWidth={2.4} />
                    <div>
                      Confirm replay for{" "}
                      <span className="font-mono font-semibold">{projectorName}</span>
                      {aggregateId.trim()
                        ? <> on aggregate <span className="font-mono">{aggregateId}</span></>
                        : null}
                      {" "}between{" "}
                      <span className="font-mono">{fromUtc.replace("T", " ")}</span>
                      {" "}and{" "}
                      <span className="font-mono">
                        {toUtc ? toUtc.replace("T", " ") : "now"}
                      </span>.
                    </div>
                  </div>
                </div>

                {error && (
                  <div className="rounded-lg bg-[#fde0db] px-3 py-2 text-[11.5px] font-medium text-[var(--color-coral)] dark:bg-[#3a1a17]">
                    {error}
                  </div>
                )}

                <div className="flex items-center justify-end gap-2 pt-1">
                  <button
                    type="button"
                    onClick={() => setConfirmStage("form")}
                    disabled={submitting}
                    className="rounded-full px-4 py-2 text-[12px] font-semibold text-[var(--color-ink-700)] hover:bg-[var(--color-ink-100)] disabled:opacity-50 dark:text-[var(--color-ink-500)] dark:hover:bg-white/10"
                  >
                    Back
                  </button>
                  <button
                    type="button"
                    onClick={submit}
                    disabled={submitting}
                    className={cn(
                      "inline-flex items-center gap-1.5 rounded-full px-4 py-2 text-[12px] font-semibold uppercase tracking-[0.06em] transition-all",
                      submitting
                        ? "cursor-not-allowed bg-[var(--color-ink-100)] text-[var(--color-ink-400)] dark:bg-white/[0.04]"
                        : "bg-[var(--color-brand-900)] text-white hover:shadow-[0_14px_36px_-12px_rgba(15,23,42,0.6)] dark:bg-[var(--color-brand-500)] dark:text-[var(--color-ink-50)]",
                    )}
                  >
                    {submitting ? (
                      <Loader2 className="h-3.5 w-3.5 animate-spin" strokeWidth={2.4} />
                    ) : (
                      <RotateCcw className="h-3.5 w-3.5" strokeWidth={2.4} />
                    )}
                    {submitting ? "Replaying…" : "Replay now"}
                  </button>
                </div>
              </div>
            )}
          </motion.div>
        </>
      )}
    </AnimatePresence>
  );
}

