"use client";

import { AlertTriangle, CheckCircle2, Loader2, ScanLine, X } from "lucide-react";
import { AnimatePresence, motion } from "motion/react";
import { useCallback, useEffect, useRef, useState } from "react";
import { OverlayBackdrop } from "@/components/primitives/overlay-backdrop";
import { getCarrier, type Carrier } from "@/lib/api/fleet-carriers";
import { cn } from "@/lib/utils";

/**
 * Resolves a scanned QR label to one carrier.
 *
 * <b>Why this is a dialog and not a field on the form it serves.</b> A USB
 * wedge scanner is a keyboard: it types into whatever holds focus and finishes
 * with Enter. Inline, that means two bugs that show up within minutes of
 * plugging one in — Enter submits the surrounding form mid-fill, and a scan
 * taken while some other input has focus silently lands its code in that input.
 * A modal owns focus for as long as it is open, which removes both.
 *
 * The Enter handler still calls preventDefault/stopPropagation: the modal is
 * the reason those are enough, not a reason to skip them.
 */

/** Mirrors Carrier.NormalizeAndValidateCode on the server. Checked here first so
 *  a product barcode off a passing box is refused instantly, with a reason,
 *  instead of costing a round trip to be told "not found". */
const CODE_PATTERN = /^[A-Z0-9][A-Z0-9._-]{0,49}$/;

/** Wedge scanners double-fire constantly — a trigger held a moment too long
 *  sends the same code twice. Anything inside this window is the same scan. */
const REPEAT_WINDOW_MS = 1500;

type Resolution =
  | { kind: "idle" }
  | { kind: "resolving" }
  | { kind: "found"; carrier: Carrier }
  | { kind: "rejected"; carrier: Carrier; reason: string }
  | { kind: "error"; message: string };

export function CarrierScanDialog({
  open,
  onClose,
  onPicked,
  knownCarriers = [],
}: {
  open: boolean;
  onClose: () => void;
  onPicked: (carrier: Carrier) => void;
  /** Already-loaded rows from the calling page. Scanned codes resolve against
   *  these first, so a scan costs no network at all in the common case; a miss
   *  still falls through to the server, which covers carriers registered since
   *  the page loaded. */
  knownCarriers?: Carrier[];
}) {
  const [value, setValue] = useState("");
  const [state, setState] = useState<Resolution>({ kind: "idle" });
  const lastScan = useRef<{ code: string; at: number } | null>(null);
  const inputRef = useRef<HTMLInputElement | null>(null);

  useEffect(() => {
    if (!open) return;
    setValue("");
    setState({ kind: "idle" });
    lastScan.current = null;
  }, [open]);

  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => e.key === "Escape" && onClose();
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [open, onClose]);

  const resolve = useCallback(
    async (raw: string) => {
      const code = raw.trim().toUpperCase();
      if (!code) return;

      // Same code, same breath: the scanner stuttered. Ignore it rather than
      // re-running the lookup and re-beeping.
      const now = Date.now();
      if (
        lastScan.current &&
        lastScan.current.code === code &&
        now - lastScan.current.at < REPEAT_WINDOW_MS
      ) {
        return;
      }
      lastScan.current = { code, at: now };

      if (!CODE_PATTERN.test(code)) {
        setState({
          kind: "error",
          message: `"${raw.trim()}" is not a carrier code. Scan a DTMS carrier label.`,
        });
        beep("bad");
        return;
      }

      const local = knownCarriers.find((c) => c.carrierCode === code);
      if (local) {
        settle(local);
        return;
      }

      setState({ kind: "resolving" });
      try {
        const found = await getCarrier(code);
        if (!found) {
          setState({ kind: "error", message: `No carrier with code ${code}.` });
          beep("bad");
          return;
        }
        settle(found);
      } catch (e) {
        setState({ kind: "error", message: (e as Error).message });
        beep("bad");
      }

      function settle(carrier: Carrier) {
        // Refuse here, with the reason, rather than letting the caller submit
        // and take a rejection from the command several steps later.
        if (carrier.status === "Retired") {
          setState({
            kind: "rejected",
            carrier,
            reason: "This carrier is retired. Un-retire it before using it.",
          });
          beep("bad");
          return;
        }
        if (carrier.status === "Maintenance") {
          setState({
            kind: "rejected",
            carrier,
            reason: carrier.maintenanceReason
              ? `Under maintenance: ${carrier.maintenanceReason}`
              : "This carrier is under maintenance.",
          });
          beep("bad");
          return;
        }
        setState({ kind: "found", carrier });
        beep("ok");
      }
    },
    [knownCarriers],
  );

  const found = state.kind === "found" ? state.carrier : null;

  return (
    <>
      {/* Sibling before AnimatePresence, never inside it — see the docblock on
          OverlayBackdrop for the stranded click-blocker this avoids. */}
      <OverlayBackdrop
        open={open}
        onClick={onClose}
        className="z-40 bg-[var(--color-ink-900)]/55 backdrop-blur-md"
      />
      <AnimatePresence>
        {open && (
          <div
            key="carrier-scan-dialog"
            className="pointer-events-none fixed inset-0 z-50 flex items-center justify-center p-4"
          >
            <motion.div
              initial={{ opacity: 0, scale: 0.94, y: 12 }}
              animate={{ opacity: 1, scale: 1, y: 0 }}
              exit={{ opacity: 0, scale: 0.96, y: 12, transition: { duration: 0.16 } }}
              transition={{ type: "spring", stiffness: 360, damping: 30 }}
              className="pointer-events-auto relative w-full max-w-md overflow-hidden rounded-[var(--radius-xl)] glass-strong"
              role="dialog"
              aria-modal="true"
              aria-label="Scan carrier"
            >
              <header className="flex items-start gap-3 px-6 pt-5">
                <span className="grid h-10 w-10 shrink-0 place-items-center rounded-full bg-[var(--color-pastel-sky)] text-[var(--color-brand-900)]">
                  <ScanLine className="h-5 w-5" strokeWidth={2.2} />
                </span>
                <h2 className="font-display mt-1 flex-1 text-[1.2rem] font-semibold text-[var(--color-ink-900)]">
                  Scan carrier
                </h2>
                <button
                  type="button"
                  onClick={onClose}
                  className="rounded-full p-2 text-[var(--color-ink-500)] transition-colors hover:bg-white/40 hover:text-[var(--color-ink-900)] dark:hover:bg-white/10"
                  aria-label="Close"
                >
                  <X className="h-4 w-4" strokeWidth={2.4} />
                </button>
              </header>

              <div className="space-y-3 px-6 pb-2 pt-4">
                <label className="block">
                  <span className="text-[10.5px] font-semibold uppercase tracking-[0.06em] text-[var(--color-ink-500)]">
                    Scanned code
                  </span>
                  <div className="relative mt-1">
                    <ScanLine
                      className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-[var(--color-ink-400)]"
                      strokeWidth={2.4}
                    />
                    <input
                      ref={inputRef}
                      autoFocus
                      value={value}
                      onChange={(e) => setValue(e.target.value)}
                      onKeyDown={(e) => {
                        if (e.key !== "Enter") return;
                        // The scanner's trailing Enter must never reach a form
                        // behind this dialog.
                        e.preventDefault();
                        e.stopPropagation();
                        void resolve(value);
                        setValue("");
                      }}
                      placeholder="Scan the sticker, or type the code"
                      spellCheck={false}
                      autoComplete="off"
                      className="w-full rounded-lg border border-white/70 bg-white/60 py-2 pl-9 pr-3 font-mono text-[13px] uppercase text-[var(--color-ink-900)] backdrop-blur-md focus:border-[var(--color-brand-500)]/30 focus:outline-none focus:ring-2 focus:ring-[var(--color-brand-500)]/40 dark:border-white/10 dark:bg-white/[0.05]"
                    />
                  </div>
                  <span className="mt-1 block text-[10.5px] text-[var(--color-ink-400)]">
                    Works with any USB barcode scanner that sends Enter on read.
                  </span>
                </label>

                {state.kind === "resolving" && (
                  <div className="flex items-center gap-2 rounded-lg bg-white/40 px-3 py-2.5 text-[12px] text-[var(--color-ink-600)] dark:bg-white/[0.04]">
                    <Loader2 className="h-3.5 w-3.5 animate-spin" />
                    Looking it up…
                  </div>
                )}

                {state.kind === "error" && (
                  <div className="flex items-start gap-2 rounded-lg bg-[var(--color-coral-soft)] px-3 py-2.5 text-[12px] font-medium text-[var(--color-coral)]">
                    <AlertTriangle className="mt-px h-3.5 w-3.5 shrink-0" strokeWidth={2.4} />
                    {state.message}
                  </div>
                )}

                {state.kind === "rejected" && (
                  <div className="rounded-lg bg-[var(--color-coral-soft)] px-3 py-2.5 text-[12px] text-[var(--color-coral)]">
                    <div className="flex items-center gap-2 font-semibold">
                      <AlertTriangle className="h-3.5 w-3.5 shrink-0" strokeWidth={2.4} />
                      <span className="font-mono">{state.carrier.carrierCode}</span>
                      <span className="rounded-full bg-white/50 px-2 py-0.5 text-[10px] uppercase tracking-[0.06em]">
                        {state.carrier.status}
                      </span>
                    </div>
                    <p className="mt-1 pl-5.5">{state.reason}</p>
                  </div>
                )}

                {found && (
                  <div className="rounded-lg bg-[var(--color-success-soft)] px-3 py-2.5 text-[12px] text-[var(--color-success)]">
                    <div className="flex items-center gap-2 font-semibold">
                      <CheckCircle2 className="h-3.5 w-3.5 shrink-0" strokeWidth={2.4} />
                      <span className="font-mono">{found.carrierCode}</span>
                      <span className="rounded-full bg-white/50 px-2 py-0.5 text-[10px] uppercase tracking-[0.06em]">
                        {found.carrierTypeCode}
                      </span>
                    </div>
                    {found.displayName && (
                      <p className="mt-1 pl-5.5 opacity-80">{found.displayName}</p>
                    )}
                  </div>
                )}
              </div>

              <footer className="flex items-center justify-end gap-2 border-t border-white/40 px-6 py-4 dark:border-white/[0.06]">
                <button
                  type="button"
                  onClick={onClose}
                  className="rounded-full px-4 py-2 text-[12px] font-semibold text-[var(--color-ink-700)] transition-colors hover:bg-white/40 dark:text-[var(--color-ink-500)] dark:hover:bg-white/10"
                >
                  Cancel
                </button>
                <button
                  type="button"
                  disabled={!found}
                  onClick={() => {
                    if (!found) return;
                    onPicked(found);
                    onClose();
                  }}
                  className={cn(
                    "inline-flex items-center gap-1.5 rounded-full px-4 py-2 text-[12px] font-semibold uppercase tracking-[0.06em] transition-all",
                    found
                      ? "bg-[var(--color-brand-900)] text-white hover:shadow-[0_10px_28px_-12px_rgba(15,23,42,0.5)] dark:bg-[var(--color-brand-500)]"
                      : "cursor-not-allowed bg-[var(--color-ink-100)] text-[var(--color-ink-400)] dark:bg-white/[0.04]",
                  )}
                >
                  <CheckCircle2 className="h-3.5 w-3.5" strokeWidth={2.4} />
                  Use this carrier
                </button>
              </footer>
            </motion.div>
          </div>
        )}
      </AnimatePresence>
    </>
  );
}

/**
 * Nobody in a warehouse is looking at the screen while they scan, so the result
 * has to be audible. Synthesised rather than shipped as an asset — two tones is
 * not worth a network request or a file in the repo.
 */
function beep(kind: "ok" | "bad"): void {
  try {
    const Ctor =
      window.AudioContext ??
      (window as unknown as { webkitAudioContext?: typeof AudioContext })
        .webkitAudioContext;
    if (!Ctor) return;
    const ctx = new Ctor();
    const osc = ctx.createOscillator();
    const gain = ctx.createGain();
    osc.type = "sine";
    osc.frequency.value = kind === "ok" ? 880 : 220;
    gain.gain.setValueAtTime(0.06, ctx.currentTime);
    gain.gain.exponentialRampToValueAtTime(0.0001, ctx.currentTime + 0.18);
    osc.connect(gain).connect(ctx.destination);
    osc.start();
    osc.stop(ctx.currentTime + 0.18);
    osc.onended = () => void ctx.close();
  } catch {
    // Audio is a courtesy, never a requirement — a browser that blocks it (no
    // user gesture yet, autoplay policy) must not break the scan.
  }
}
