"use client";

import { AlertTriangle, Loader2, X } from "lucide-react";
import { AnimatePresence, motion } from "motion/react";
import { useState } from "react";
import { OverlayBackdrop } from "@/components/primitives/overlay-backdrop";
import {
  deleteCarrierTypeProfile,
  type CarrierTypeProfile,
} from "@/lib/api/fleet-carrier-types";
import { cn } from "@/lib/utils";

/**
 * Confirms the one destructive action on the catalogue. Kept separate from the
 * form dialog for the same reason CarrierActionDialog is separate from
 * CarrierFormDialog: editing and destroying should never be one click apart,
 * and the consequence has to be spelled out before the click, not after.
 */
export function CarrierTypeDeleteDialog({
  carrierType,
  onClose,
  onDeleted,
}: {
  /** null → closed. */
  carrierType: CarrierTypeProfile | null;
  onClose: () => void;
  onDeleted: () => void;
}) {
  const open = carrierType !== null;
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const run = async () => {
    if (!carrierType) return;
    setBusy(true);
    setError(null);
    try {
      await deleteCarrierTypeProfile(carrierType.code);
      onDeleted();
      onClose();
    } catch (e) {
      // A blocked delete arrives as 409 carrying the domain's own explanation
      // ("used by 4 carrier(s) — move or delete them first"), so it is shown
      // verbatim rather than replaced with a generic failure.
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  };

  return (
    <>
      <OverlayBackdrop
        open={open}
        onClick={() => !busy && onClose()}
        className="z-40 bg-[var(--color-ink-900)]/55 backdrop-blur-md"
      />
      <AnimatePresence>
        {open && carrierType && (
          <div
            key="carrier-type-delete-dialog"
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
                <h2 className="font-display mt-1 flex-1 text-[1.2rem] font-semibold text-[var(--color-ink-900)]">
                  Delete carrier type
                </h2>
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
                <p className="text-[12.5px] leading-relaxed text-[var(--color-ink-600)]">
                  {carrierType.code} is removed permanently and cannot be recovered
                  {carrierType.photoCount > 0
                    ? `, together with its ${carrierType.photoCount} photo${carrierType.photoCount === 1 ? "" : "s"}`
                    : ""}
                  . Only a type no carrier is using can be deleted.
                </p>

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
                  onClick={() => void run()}
                  disabled={busy}
                  whileHover={busy ? {} : { y: -1 }}
                  whileTap={busy ? {} : { scale: 0.97 }}
                  className={cn(
                    "inline-flex items-center gap-1.5 rounded-full px-4 py-2 text-[12px] font-semibold transition-all",
                    busy
                      ? "cursor-not-allowed bg-[var(--color-ink-100)] text-[var(--color-ink-400)] dark:bg-white/[0.04]"
                      : "bg-[var(--color-coral)] text-white hover:shadow-[0_14px_36px_-12px_rgba(220,80,80,0.6)]",
                  )}
                >
                  {busy && <Loader2 className="h-3.5 w-3.5 animate-spin" strokeWidth={2.4} />}
                  {busy ? "Working…" : "Delete permanently"}
                </motion.button>
              </footer>
            </motion.div>
          </div>
        )}
      </AnimatePresence>
    </>
  );
}
