"use client";

import { AlertTriangle, Loader2, X } from "lucide-react";
import { AnimatePresence, motion } from "motion/react";
import { useEffect, useState } from "react";
import { raiseTripException } from "@/lib/api/trips";
import { cn } from "@/lib/utils";

// Dispatcher-side "flag an exception" on a trip. POSTs to
// /trips/{id}/exceptions; the trip stays in its current status (an
// exception is an annotation, not a lifecycle transition). Parent refetches
// on success so the raised exception surfaces in the activity timeline.

const SEVERITIES = ["Low", "Medium", "High", "Critical"] as const;
// Common codes offered as a datalist — free text is still allowed so ops
// can flag something new without a code-list deploy.
const COMMON_CODES = [
  "BLOCKED",
  "DAMAGED",
  "MISSING_ITEM",
  "WRONG_LOCATION",
  "ROBOT_STUCK",
  "ACCESS_DENIED",
  "OTHER",
];

export function RaiseExceptionDialog({
  open,
  tripId,
  onClose,
  onRaised,
}: {
  open: boolean;
  tripId: string;
  onClose: () => void;
  onRaised: () => void;
}) {
  const [code, setCode] = useState("");
  const [severity, setSeverity] = useState<string>("Medium");
  const [detail, setDetail] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (open) {
      setCode("");
      setSeverity("Medium");
      setDetail("");
      setBusy(false);
      setError(null);
    }
  }, [open]);

  const canSubmit = code.trim().length > 0 && detail.trim().length > 0 && !busy;

  const handleSubmit = async () => {
    if (!canSubmit) return;
    setBusy(true);
    setError(null);
    try {
      await raiseTripException(tripId, {
        code: code.trim(),
        severity,
        detail: detail.trim(),
      });
      onRaised();
      onClose();
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setBusy(false);
    }
  };

  return (
    <AnimatePresence>
      {open && (
        <>
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            onClick={() => !busy && onClose()}
            className="fixed inset-0 z-40 bg-[var(--color-ink-900)]/55 backdrop-blur-md"
          />
          <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
            <motion.div
              initial={{ opacity: 0, scale: 0.94, y: 12 }}
              animate={{ opacity: 1, scale: 1, y: 0 }}
              exit={{ opacity: 0, scale: 0.96, y: 12, transition: { duration: 0.16 } }}
              transition={{ type: "spring", stiffness: 360, damping: 30 }}
              className="relative w-full max-w-md overflow-hidden rounded-[var(--radius-xl)] glass-strong"
            >
              <header className="flex items-start gap-3 px-6 pt-5">
                <span className="grid h-10 w-10 shrink-0 place-items-center rounded-full bg-[var(--color-amber-soft)] text-[var(--color-amber)]">
                  <AlertTriangle className="h-5 w-5" strokeWidth={2.2} />
                </span>
                <div className="min-w-0 flex-1">
                  <div className="text-[10.5px] font-semibold uppercase tracking-[0.12em] text-[var(--color-ink-400)]">
                    Trip #{tripId.slice(0, 8).toUpperCase()}
                  </div>
                  <h2 className="font-display mt-1 text-[1.2rem] font-semibold text-[var(--color-ink-900)]">
                    Raise an exception
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

              <div className="space-y-3 px-6 pb-2 pt-4">
                <div className="flex gap-3">
                  <label className="flex flex-1 flex-col gap-1">
                    <span className={labelCls}>Code</span>
                    <input
                      list="exc-codes"
                      value={code}
                      onChange={(e) => setCode(e.target.value)}
                      placeholder="BLOCKED"
                      className={inputCls}
                    />
                    <datalist id="exc-codes">
                      {COMMON_CODES.map((c) => (
                        <option key={c} value={c} />
                      ))}
                    </datalist>
                  </label>
                  <label className="flex w-32 flex-col gap-1">
                    <span className={labelCls}>Severity</span>
                    <select
                      value={severity}
                      onChange={(e) => setSeverity(e.target.value)}
                      className={inputCls}
                    >
                      {SEVERITIES.map((s) => (
                        <option key={s} value={s}>
                          {s}
                        </option>
                      ))}
                    </select>
                  </label>
                </div>
                <label className="flex flex-col gap-1">
                  <span className={labelCls}>Detail</span>
                  <textarea
                    value={detail}
                    onChange={(e) => setDetail(e.target.value)}
                    rows={3}
                    placeholder="What happened?"
                    className={cn(inputCls, "resize-none py-2 leading-relaxed")}
                  />
                </label>
                {error && (
                  <div className="rounded-md bg-[var(--color-coral-soft)] px-3 py-2 text-[11.5px] font-medium text-[var(--color-coral)]">
                    {error}
                  </div>
                )}
              </div>

              <footer className="flex items-center justify-end gap-2 border-t border-white/40 px-6 py-4 dark:border-white/[0.06]">
                <button
                  type="button"
                  onClick={() => !busy && onClose()}
                  className="rounded-full bg-white/40 px-4 py-2 text-[12px] font-semibold text-[var(--color-ink-700)] transition-colors hover:bg-white/70 dark:bg-white/[0.05] dark:hover:bg-white/[0.1]"
                >
                  Cancel
                </button>
                <motion.button
                  type="button"
                  onClick={() => void handleSubmit()}
                  disabled={!canSubmit}
                  whileHover={canSubmit ? { y: -1 } : {}}
                  whileTap={canSubmit ? { scale: 0.97 } : {}}
                  className={cn(
                    "inline-flex items-center gap-1.5 rounded-full px-4 py-2 text-[12px] font-semibold transition-all",
                    canSubmit
                      ? "bg-[var(--color-amber)] text-white hover:shadow-[0_14px_36px_-12px_rgba(217,119,6,0.55)]"
                      : "bg-[var(--color-ink-100)] text-[var(--color-ink-400)] cursor-not-allowed dark:bg-white/[0.04]",
                  )}
                >
                  {busy ? (
                    <Loader2 className="h-3.5 w-3.5 animate-spin" strokeWidth={2.4} />
                  ) : (
                    <AlertTriangle className="h-3.5 w-3.5" strokeWidth={2.4} />
                  )}
                  {busy ? "Raising…" : "Raise exception"}
                </motion.button>
              </footer>
            </motion.div>
          </div>
        </>
      )}
    </AnimatePresence>
  );
}

const labelCls =
  "text-[10px] font-semibold uppercase tracking-[0.1em] text-[var(--color-ink-400)]";
const inputCls =
  "h-9 rounded-md border border-white/70 bg-white/60 px-2.5 text-[12.5px] text-[var(--color-ink-900)] backdrop-blur-md focus:border-[var(--color-brand-500)]/30 focus:outline-none focus:ring-2 focus:ring-[var(--color-brand-500)]/40 dark:border-white/10 dark:bg-white/[0.05]";
