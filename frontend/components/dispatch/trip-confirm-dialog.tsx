"use client";

import { Check, Loader2, Repeat2, X } from "lucide-react";
import { motion } from "motion/react";
import { ModalLayer } from "@/components/primitives/modal-layer";
import { useEffect, useRef, useState } from "react";
import type { TripStatus } from "@/lib/api/trips";
import { cn } from "@/lib/utils";
import { T, spring } from "@/lib/motion";

export const DEFAULT_CANCEL_REASON = "Cancelled by operator";

// Confirmation for the two lifecycle actions that can't be taken back
// from the action bar: Cancel stops the vendor mission, Retry dispatches
// a new trip. Same shell as PassRobotDialog: confirm is a button press
// only (intent="irreversible" — focus on Keep, no Enter-to-submit).
export function TripConfirmDialog({
  open,
  kind,
  tripId,
  status,
  vendorVehicleKey,
  onClose,
  onConfirm,
}: {
  open: boolean;
  kind: "cancel" | "retry";
  tripId: string;
  status: TripStatus;
  vendorVehicleKey?: string | null;
  onClose: () => void;
  /** Cancel receives the reason (default when left blank). Throw to keep
   *  the dialog open and show the error. */
  onConfirm: (reason: string) => Promise<void>;
}) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [reason, setReason] = useState("");
  // Brief success tick before the dialog closes itself.
  const [done, setDone] = useState(false);
  const closeTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => {
    if (open) {
      setBusy(false);
      setError(null);
      setReason("");
      setDone(false);
    }
  }, [open]);

  useEffect(() => {
    return () => {
      if (closeTimerRef.current) clearTimeout(closeTimerRef.current);
    };
  }, []);

  // Focus opens on Keep (intent="irreversible"); Esc closes only this
  // dialog, not the trip drawer underneath (layer stack).
  const keepRef = useRef<HTMLButtonElement>(null);

  const handleConfirm = async () => {
    setBusy(true);
    setError(null);
    try {
      await onConfirm(reason.trim() || DEFAULT_CANCEL_REASON);
      // The action has already gone through; the ~350ms tick only delays
      // the close, so the operator sees it landed before the dialog goes.
      setDone(true);
      closeTimerRef.current = setTimeout(onClose, 350);
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setBusy(false);
    }
  };

  const isCancel = kind === "cancel";
  const shortTrip = tripId.slice(0, 8).toUpperCase();

  return (
    <ModalLayer
      open={open}
      onClose={onClose}
      dismissible={!busy && !done}
      intent="irreversible"
      safeRef={keepRef}
      backdropClassName="bg-[var(--color-ink-900)]/55 backdrop-blur-md"
    >
      {({ panelProps, titleId, panelStyle }) => (
          <div
            key={`trip-${kind}-dialog`}
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
                    isCancel
                      ? "bg-[var(--color-coral-soft)] text-[var(--color-coral)]"
                      : "bg-[var(--color-pastel-lavender)] text-[var(--color-pastel-lavender-ink)]",
                  )}
                >
                  {isCancel ? (
                    <X className="h-5 w-5" strokeWidth={2.4} />
                  ) : (
                    <Repeat2 className="h-5 w-5" strokeWidth={2.2} />
                  )}
                </span>
                <div className="min-w-0 flex-1">
                  <div className="text-[10.5px] font-semibold uppercase tracking-[0.12em] text-[var(--color-ink-400)]">
                    {isCancel ? "Cancel trip" : "Retry trip"}
                  </div>
                  <h2
                    id={titleId}
                    className="font-display mt-1 text-[1.2rem] font-semibold text-[var(--color-ink-900)] truncate"
                  >
                    {isCancel ? "Cancel this trip?" : "Dispatch this trip again?"}
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
                    Trip
                  </dt>
                  <dd className="text-[var(--color-ink-700)]">
                    #{shortTrip} <span className="text-[var(--color-ink-400)]">— {status}</span>
                  </dd>
                  {vendorVehicleKey && (
                    <>
                      <dt className="font-semibold uppercase tracking-[0.08em] text-[var(--color-ink-400)] text-[10.5px] pt-0.5">
                        Robot
                      </dt>
                      <dd className="font-semibold text-[var(--color-ink-900)] font-mono">
                        {vendorVehicleKey}
                      </dd>
                    </>
                  )}
                </dl>
                <p className="mt-3 text-[12.5px] leading-relaxed text-[var(--color-ink-600)]">
                  {isCancel ? (
                    <>
                      ระบบจะสั่งยกเลิกงานนี้กับ vendor ทันที หุ่นจะหยุดทำงานนี้ และ Trip จะเป็น{" "}
                      <span className="font-semibold">Cancelled</span> — ถ้าต้องการทำต่อต้องกด
                      Retry เพื่อสร้าง Trip ใหม่
                    </>
                  ) : (
                    <>
                      ระบบจะสร้าง Trip ใหม่จาก Trip ที่ถูกยกเลิกนี้ แล้วส่งงานให้หุ่นอีกครั้งทันที
                    </>
                  )}
                </p>
                {isCancel && (
                  <label className="mt-3 block">
                    <span className="block text-[10.5px] font-semibold uppercase tracking-[0.08em] text-[var(--color-ink-400)]">
                      Reason <span className="normal-case tracking-normal">(optional)</span>
                    </span>
                    <textarea
                      value={reason}
                      onChange={(e) => setReason(e.target.value)}
                      disabled={busy}
                      rows={2}
                      maxLength={500}
                      placeholder={DEFAULT_CANCEL_REASON}
                      className="mt-1 w-full resize-none rounded-md border border-[var(--color-ink-100)] bg-white/65 px-3 py-2 text-[12.5px] text-[var(--color-ink-900)] outline-none placeholder:text-[var(--color-ink-400)] focus:border-[var(--color-brand-500)] dark:bg-white/[0.04]"
                    />
                  </label>
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
                  {isCancel ? "Keep trip" : "Not now"}
                </button>
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
                      : isCancel
                        ? "bg-[var(--color-coral)] text-white"
                        : "bg-[var(--color-brand-500)] text-white",
                  )}
                >
                  {done ? (
                    <Check className="h-3.5 w-3.5" strokeWidth={2.6} />
                  ) : busy ? (
                    <Loader2 className="h-3.5 w-3.5 animate-spin" strokeWidth={2.4} />
                  ) : isCancel ? (
                    <X className="h-3.5 w-3.5" strokeWidth={2.4} />
                  ) : (
                    <Repeat2 className="h-3.5 w-3.5" strokeWidth={2.4} />
                  )}
                  {done
                    ? isCancel
                      ? "Cancelled"
                      : "Retried"
                    : busy
                    ? isCancel
                      ? "Cancelling…"
                      : "Retrying…"
                    : isCancel
                      ? "Cancel trip"
                      : "Retry trip"}
                </motion.button>
              </footer>
            </motion.div>
          </div>
      )}
    </ModalLayer>
  );
}
