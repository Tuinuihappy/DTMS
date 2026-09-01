"use client";

import { AlertTriangle, Loader2, X } from "lucide-react";
import { AnimatePresence, motion } from "motion/react";
import { useEffect, useState } from "react";
import { OverlayBackdrop } from "@/components/primitives/overlay-backdrop";
import {
  deleteCarrier,
  moveCarrier,
  retireCarrier,
  returnCarrierToService,
  setCarrierMaintenance,
  unretireCarrier,
  type Carrier,
} from "@/lib/api/fleet-carriers";
import { cn } from "@/lib/utils";

export type CarrierAction =
  | "maintenance"
  | "return-to-service"
  | "location"
  | "retire"
  | "unretire"
  | "delete";

type Spec = {
  title: string;
  /** Rendered above the input. Where a consequence is irreversible it is
   *  spelled out here rather than left for the user to discover. */
  body?: (carrier: Carrier) => string;
  inputLabel?: string;
  placeholder?: string;
  /** A required input disables the confirm button until filled. */
  inputRequired?: boolean;
  confirmLabel: string;
  tone: "normal" | "warn" | "danger";
};

// Retire and delete both remove a carrier from circulation, but they are not
// interchangeable and the wording has to make that obvious before the click:
// retire keeps the history and holds the code forever; delete erases the row
// and frees the code, and is only possible while there is no history at all.
const SPECS: Record<CarrierAction, Spec> = {
  maintenance: {
    title: "Send for maintenance",
    inputLabel: "Reason",
    placeholder: "Broken wheel, bent frame…",
    inputRequired: true,
    confirmLabel: "Send for maintenance",
    tone: "normal",
  },
  "return-to-service": {
    title: "Return to service",
    inputLabel: "Outcome",
    placeholder: "optional — what was done",
    confirmLabel: "Return to service",
    tone: "normal",
  },
  location: {
    title: "Update location",
    body: () => "Records where the carrier was last seen, with the time.",
    inputLabel: "Location",
    placeholder: "DOCK-A, WORKSHOP…",
    confirmLabel: "Save location",
    tone: "normal",
  },
  retire: {
    title: "Retire carrier",
    body: (c) =>
      `${c.carrierCode} keeps its history, and the code stays reserved forever — no future carrier can reuse it. ` +
      "If this carrier was registered by mistake, delete it instead.",
    inputLabel: "Reason",
    placeholder: "Bent frame, end of life…",
    inputRequired: true,
    confirmLabel: "Retire",
    tone: "warn",
  },
  unretire: {
    title: "Bring back into use",
    body: (c) => `${c.carrierCode} returns to Available. Its history stays attached.`,
    confirmLabel: "Bring back",
    tone: "normal",
  },
  delete: {
    title: "Delete carrier",
    body: (c) =>
      `${c.carrierCode} is removed permanently and cannot be recovered. The code becomes available again. ` +
      "Only carriers with no history at all can be deleted — if this one has been used, retire it instead.",
    confirmLabel: "Delete permanently",
    tone: "danger",
  },
};

export function CarrierActionDialog({
  action,
  carrier,
  onClose,
  onDone,
}: {
  action: CarrierAction | null;
  carrier: Carrier | null;
  onClose: () => void;
  onDone: () => void;
}) {
  const open = action !== null && carrier !== null;
  const spec = action ? SPECS[action] : null;

  const [value, setValue] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (open) {
      setValue(action === "location" ? (carrier?.currentLocationCode ?? "") : "");
      setBusy(false);
      setError(null);
    }
  }, [open, action, carrier]);

  if (!spec || !carrier || !action) {
    return (
      <OverlayBackdrop
        open={false}
        onClick={onClose}
        className="z-40 bg-[var(--color-ink-900)]/55 backdrop-blur-md"
      />
    );
  }

  const canConfirm = !spec.inputRequired || value.trim() !== "";

  const run = async () => {
    if (!canConfirm) return;
    setBusy(true);
    setError(null);
    try {
      const code = carrier.carrierCode;
      switch (action) {
        case "maintenance":
          await setCarrierMaintenance(code, value.trim());
          break;
        case "return-to-service":
          await returnCarrierToService(code, value.trim() || null);
          break;
        case "location":
          await moveCarrier(code, value.trim() || null);
          break;
        case "retire":
          await retireCarrier(code, value.trim());
          break;
        case "unretire":
          await unretireCarrier(code);
          break;
        case "delete":
          await deleteCarrier(code);
          break;
      }
      onDone();
      onClose();
    } catch (e) {
      // A blocked delete arrives as 409 carrying the domain's own explanation
      // ("has 3 maintenance record(s) — retire it instead"), so it is shown
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
        {open && (
          <div
            key="carrier-action-dialog"
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
                <span
                  className={cn(
                    "grid h-10 w-10 shrink-0 place-items-center rounded-full",
                    spec.tone === "danger"
                      ? "bg-[var(--color-coral-soft)] text-[var(--color-coral)]"
                      : spec.tone === "warn"
                        ? "bg-[var(--color-pastel-butter)] text-[var(--color-ink-800)]"
                        : "bg-[var(--color-pastel-mint)] text-[var(--color-brand-900)]",
                  )}
                >
                  <AlertTriangle className="h-5 w-5" strokeWidth={2.2} />
                </span>
                <h2 className="font-display mt-1 flex-1 text-[1.2rem] font-semibold text-[var(--color-ink-900)]">
                  {spec.title}
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
                {spec.body && (
                  <p className="text-[12.5px] leading-relaxed text-[var(--color-ink-600)]">
                    {spec.body(carrier)}
                  </p>
                )}

                {spec.inputLabel && (
                  <label className="flex flex-col gap-1">
                    <span className="text-[10px] font-semibold uppercase tracking-[0.1em] text-[var(--color-ink-400)]">
                      {spec.inputLabel}
                    </span>
                    <input
                      value={value}
                      onChange={(e) => setValue(e.target.value)}
                      placeholder={spec.placeholder}
                      className="h-9 rounded-md border border-white/70 bg-white/60 px-2.5 text-[12.5px] text-[var(--color-ink-900)] backdrop-blur-md focus:border-[var(--color-brand-500)]/30 focus:outline-none focus:ring-2 focus:ring-[var(--color-brand-500)]/40 dark:border-white/10 dark:bg-white/[0.05]"
                    />
                  </label>
                )}

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
                  disabled={!canConfirm || busy}
                  whileHover={canConfirm && !busy ? { y: -1 } : {}}
                  whileTap={canConfirm && !busy ? { scale: 0.97 } : {}}
                  className={cn(
                    "inline-flex items-center gap-1.5 rounded-full px-4 py-2 text-[12px] font-semibold transition-all",
                    !canConfirm || busy
                      ? "cursor-not-allowed bg-[var(--color-ink-100)] text-[var(--color-ink-400)] dark:bg-white/[0.04]"
                      : spec.tone === "danger"
                        ? "bg-[var(--color-coral)] text-white hover:shadow-[0_14px_36px_-12px_rgba(220,80,80,0.6)]"
                        : "bg-[var(--color-brand-900)] text-white hover:shadow-[0_14px_36px_-12px_rgba(15,23,42,0.6)] dark:bg-[var(--color-brand-500)]",
                  )}
                >
                  {busy && <Loader2 className="h-3.5 w-3.5 animate-spin" strokeWidth={2.4} />}
                  {busy ? "Working…" : spec.confirmLabel}
                </motion.button>
              </footer>
            </motion.div>
          </div>
        )}
      </AnimatePresence>
    </>
  );
}
