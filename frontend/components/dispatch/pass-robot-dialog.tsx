"use client";

import { Bot, ChevronsRight, Loader2, X } from "lucide-react";
import { AnimatePresence, motion } from "motion/react";
import { useEffect, useState } from "react";
import { acknowledgeRobotPass } from "@/lib/api/trips";
import { cn } from "@/lib/utils";

// Confirmation dialog for RIOT3 PASS — operator acknowledges a robot
// waiting at a checkpoint. Trip stays InProgress on success; we still
// confirm because PASS triggers physical robot movement and the operator
// needs visible context (which robot, which trip) before committing.
export function PassRobotDialog({
  open,
  tripId,
  vendorVehicleKey,
  onClose,
  onPassed,
}: {
  open: boolean;
  tripId: string;
  vendorVehicleKey: string;
  onClose: () => void;
  onPassed: () => void;
}) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (open) {
      setBusy(false);
      setError(null);
    }
  }, [open]);

  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape" && !busy) onClose();
      if (e.key === "Enter" && !busy) void handleConfirm();
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, busy]);

  const handleConfirm = async () => {
    setBusy(true);
    setError(null);
    try {
      await acknowledgeRobotPass(tripId);
      onPassed();
      onClose();
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setBusy(false);
    }
  };

  const shortTrip = tripId.slice(0, 8).toUpperCase();

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
                <span className="grid h-10 w-10 shrink-0 place-items-center rounded-full bg-[var(--color-success-soft)] text-[var(--color-success)]">
                  <Bot className="h-5 w-5" strokeWidth={2.2} />
                </span>
                <div className="min-w-0 flex-1">
                  <div className="text-[10.5px] font-semibold uppercase tracking-[0.12em] text-[var(--color-ink-400)]">
                    Robot checkpoint
                  </div>
                  <h2 className="font-display mt-1 text-[1.2rem] font-semibold text-[var(--color-ink-900)] truncate">
                    Pass robot through?
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
                <dl className="grid grid-cols-[80px_1fr] gap-x-3 gap-y-1.5 text-[12.5px]">
                  <dt className="font-semibold uppercase tracking-[0.08em] text-[var(--color-ink-400)] text-[10.5px] pt-0.5">
                    Robot
                  </dt>
                  <dd className="font-semibold text-[var(--color-ink-900)] font-mono">
                    {vendorVehicleKey}
                  </dd>
                  <dt className="font-semibold uppercase tracking-[0.08em] text-[var(--color-ink-400)] text-[10.5px] pt-0.5">
                    Trip
                  </dt>
                  <dd className="text-[var(--color-ink-700)]">
                    #{shortTrip} <span className="text-[var(--color-ink-400)]">— InProgress</span>
                  </dd>
                </dl>
                <p className="mt-3 text-[12.5px] leading-relaxed text-[var(--color-ink-600)]">
                  Robot จะเดินไปทำงานขั้นถัดไปทันที สถานะ Trip ยังคงเป็น{" "}
                  <span className="font-semibold">InProgress</span>
                </p>
                {error && (
                  <div className="mt-3 rounded-md bg-[#fde0db] px-3 py-2 text-[11.5px] font-medium text-[var(--color-coral)] dark:bg-[#3a1a17]">
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
                  onClick={() => !busy && void handleConfirm()}
                  disabled={busy}
                  whileHover={!busy ? { y: -1 } : {}}
                  whileTap={!busy ? { scale: 0.97 } : {}}
                  className={cn(
                    "inline-flex items-center gap-1.5 rounded-full px-4 py-2 text-[12px] font-semibold transition-all",
                    busy
                      ? "bg-[var(--color-ink-100)] text-[var(--color-ink-400)] cursor-not-allowed dark:bg-white/[0.04]"
                      : "bg-[var(--color-success)] text-white hover:shadow-[0_14px_36px_-12px_rgba(64,158,121,0.55)]",
                  )}
                >
                  {busy ? (
                    <Loader2 className="h-3.5 w-3.5 animate-spin" strokeWidth={2.4} />
                  ) : (
                    <ChevronsRight className="h-3.5 w-3.5" strokeWidth={2.4} />
                  )}
                  {busy ? "Passing…" : "PASS"}
                </motion.button>
              </footer>
            </motion.div>
          </div>
        </>
      )}
    </AnimatePresence>
  );
}
