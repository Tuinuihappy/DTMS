"use client";

import { useEffect, useRef, useState, type ReactNode } from "react";
import { motion } from "motion/react";
import { AlertTriangle, Check, Loader2, Trash2, X } from "lucide-react";
import { ModalLayer } from "@/components/primitives/modal-layer";
import { T, spring } from "@/lib/motion";
import { cn } from "@/lib/utils";

// Confirmation for the two station actions that can't be taken back from
// the directory: deactivating a large held batch after a RIOT3 sync, and
// purging (hard-deleting) stations RIOT3 removed. Same shell as
// TripConfirmDialog (intent="irreversible" — focus on the safe button).
// onConfirm may return a summary to show in place of the list; otherwise
// the dialog closes itself after a brief tick.
export function StationConfirmDialog({
  open,
  tone,
  eyebrow,
  title,
  description,
  items,
  confirmLabel,
  busyLabel,
  onClose,
  onConfirm,
}: {
  open: boolean;
  tone: "danger" | "warn";
  eyebrow: string;
  title: string;
  description: ReactNode;
  /** Station codes the action applies to. */
  items: string[];
  confirmLabel: string;
  busyLabel: string;
  onClose: () => void;
  /** Throw to keep the dialog open and show the error. */
  onConfirm: () => Promise<ReactNode | void>;
}) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [done, setDone] = useState(false);
  const [summary, setSummary] = useState<ReactNode | null>(null);
  const closeTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const keepRef = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    if (open) {
      setBusy(false);
      setError(null);
      setDone(false);
      setSummary(null);
    }
  }, [open]);

  useEffect(() => {
    return () => {
      if (closeTimerRef.current) clearTimeout(closeTimerRef.current);
    };
  }, []);

  const handleConfirm = async () => {
    setBusy(true);
    setError(null);
    try {
      const result = await onConfirm();
      setDone(true);
      if (result) setSummary(result);
      else closeTimerRef.current = setTimeout(onClose, 350);
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setBusy(false);
    }
  };

  const danger = tone === "danger";

  return (
    <ModalLayer
      open={open}
      onClose={onClose}
      dismissible={!busy}
      intent="irreversible"
      safeRef={keepRef}
      backdropClassName="bg-[var(--color-ink-900)]/55 backdrop-blur-md"
    >
      {({ panelProps, titleId, panelStyle }) => (
        <div
          key="station-confirm-dialog"
          style={panelStyle}
          className="pointer-events-none fixed inset-0 flex items-center justify-center p-4"
        >
          <motion.div
            {...panelProps}
            initial={{ opacity: 0, scale: 0.94, y: 12 }}
            animate={{ opacity: 1, scale: 1, y: 0 }}
            exit={{ opacity: 0, scale: 0.96, y: 12, transition: T.exit }}
            transition={spring.dialog}
            className="pointer-events-auto relative w-full max-w-md overflow-hidden rounded-[var(--radius-xl)] glass-strong outline-none"
          >
            <header className="flex items-start gap-3 px-6 pt-5">
              <span
                className={cn(
                  "grid h-10 w-10 shrink-0 place-items-center rounded-full",
                  danger
                    ? "bg-[var(--color-coral-soft)] text-[var(--color-coral)]"
                    : "bg-[var(--color-amber-soft)] text-[var(--color-amber)]",
                )}
              >
                {danger ? (
                  <Trash2 className="h-5 w-5" strokeWidth={2.2} />
                ) : (
                  <AlertTriangle className="h-5 w-5" strokeWidth={2.2} />
                )}
              </span>
              <div className="min-w-0 flex-1">
                <div className="text-[10.5px] font-semibold uppercase tracking-[0.12em] text-[var(--color-ink-400)]">
                  {eyebrow}
                </div>
                <h2
                  id={titleId}
                  className="font-display mt-1 text-[1.2rem] font-semibold text-[var(--color-ink-900)]"
                >
                  {title}
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
              {summary ? (
                <div className="text-[12.5px] leading-relaxed text-[var(--color-ink-700)]">{summary}</div>
              ) : (
                <>
                  <div className="text-[12.5px] leading-relaxed text-[var(--color-ink-600)]">{description}</div>
                  {items.length > 0 && (
                    <ul className="mt-3 max-h-40 overflow-y-auto rounded-md bg-white/45 px-3 py-2 font-mono text-[11.5px] text-[var(--color-ink-800)] dark:bg-white/[0.04]">
                      {items.map((code) => (
                        <li key={code} className="py-0.5">
                          {code}
                        </li>
                      ))}
                    </ul>
                  )}
                </>
              )}
              {error && (
                <div className="mt-3 rounded-md bg-[var(--color-coral-soft)] px-3 py-2 text-[11.5px] font-medium text-[var(--color-coral)]">
                  {error}
                </div>
              )}
            </div>

            <footer className="flex items-center justify-end gap-2 border-t border-white/40 px-6 py-4 dark:border-white/[0.06]">
              <button
                ref={keepRef}
                type="button"
                onClick={() => !busy && onClose()}
                className="rounded-full bg-white/40 px-4 py-2 text-[12px] font-semibold text-[var(--color-ink-700)] transition-colors hover:bg-white/70 dark:bg-white/[0.05] dark:hover:bg-white/[0.1]"
              >
                {summary ? "Close" : "Not now"}
              </button>
              {!summary && (
                <motion.button
                  type="button"
                  onClick={() => !busy && !done && void handleConfirm()}
                  disabled={busy || done}
                  aria-busy={busy || undefined}
                  whileHover={!busy && !done ? { y: -1 } : {}}
                  whileTap={!busy && !done ? { scale: 0.97 } : {}}
                  className={cn(
                    "inline-flex items-center gap-1.5 rounded-full px-4 py-2 text-[12px] font-semibold transition-[background-color,border-color,box-shadow,color,opacity]",
                    done
                      ? "bg-[var(--color-success)] text-white"
                      : busy
                        ? "bg-[var(--color-ink-100)] text-[var(--color-ink-400)] cursor-not-allowed dark:bg-white/[0.04]"
                        : danger
                          ? "bg-[var(--color-coral)] text-white"
                          : "bg-[var(--color-amber)] text-white",
                  )}
                >
                  {done ? (
                    <Check className="h-3.5 w-3.5" strokeWidth={2.6} />
                  ) : busy ? (
                    <Loader2 className="h-3.5 w-3.5 animate-spin" strokeWidth={2.4} />
                  ) : danger ? (
                    <Trash2 className="h-3.5 w-3.5" strokeWidth={2.4} />
                  ) : (
                    <AlertTriangle className="h-3.5 w-3.5" strokeWidth={2.4} />
                  )}
                  {busy ? busyLabel : confirmLabel}
                </motion.button>
              )}
            </footer>
          </motion.div>
        </div>
      )}
    </ModalLayer>
  );
}
