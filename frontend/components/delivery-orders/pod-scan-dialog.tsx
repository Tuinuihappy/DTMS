"use client";

import { Barcode, CheckCircle2, Keyboard, PenLine, ScanLine, X } from "lucide-react";
import { AnimatePresence, motion } from "motion/react";
import { useEffect, useRef, useState } from "react";
import { cn } from "@/lib/utils";
import type { PodMethod, PodScanType } from "@/lib/api/delivery-orders";

/**
 * POD scan dialog. Supports four methods chosen via tab strip:
 *   Confirm    — operator vouches with name + optional note, no reference
 *   Manual     — operator types a barcode / SKU code reference
 *   Barcode    — camera scan (delegated to native input scanner where
 *                available; uses same text-input UX otherwise so paste
 *                from a USB scanner Just Works)
 *   Signature  — drawn on a small canvas, hash sent as reference
 *
 * On submit calls onConfirm({ method, reference }) — the parent owns
 * the API call so it can refresh the order in one place.
 */
export function PodScanDialog({
  orderRef,
  itemId,
  itemLabel,
  currentUser,
  open,
  onClose,
  onConfirm,
  busy,
  error,
  scanType = "Drop",
}: {
  orderRef: string | null;
  itemId: string | null;
  itemLabel: string | null;
  currentUser: string | null;
  open: boolean;
  onClose: () => void;
  onConfirm: (input: { scannedBy: string; method: PodMethod; reference: string | null; scanType: PodScanType }) => Promise<void> | void;
  busy?: boolean;
  error?: string | null;
  scanType?: PodScanType;
}) {
  const [method, setMethod] = useState<PodMethod>("Confirm");
  const [scannedBy, setScannedBy] = useState(currentUser ?? "");
  const [reference, setReference] = useState("");
  const sigRef = useRef<HTMLCanvasElement | null>(null);

  useEffect(() => {
    if (!open) return;
    setMethod("Confirm");
    setScannedBy(currentUser ?? "");
    setReference("");
  }, [open, currentUser]);

  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => e.key === "Escape" && onClose();
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [open, onClose]);

  // Signature pad: minimal mouse/touch drawing. Hash on submit.
  useEffect(() => {
    if (!open || method !== "Signature") return;
    const canvas = sigRef.current;
    if (!canvas) return;
    const ctx = canvas.getContext("2d");
    if (!ctx) return;
    ctx.clearRect(0, 0, canvas.width, canvas.height);
    ctx.strokeStyle = "#1a1a1a";
    ctx.lineWidth = 2;
    ctx.lineCap = "round";
    let drawing = false;
    const pt = (e: PointerEvent) => {
      const r = canvas.getBoundingClientRect();
      return { x: e.clientX - r.left, y: e.clientY - r.top };
    };
    const down = (e: PointerEvent) => {
      drawing = true;
      const p = pt(e);
      ctx.beginPath();
      ctx.moveTo(p.x, p.y);
    };
    const move = (e: PointerEvent) => {
      if (!drawing) return;
      const p = pt(e);
      ctx.lineTo(p.x, p.y);
      ctx.stroke();
    };
    const up = () => { drawing = false; };
    canvas.addEventListener("pointerdown", down);
    canvas.addEventListener("pointermove", move);
    canvas.addEventListener("pointerup", up);
    canvas.addEventListener("pointerleave", up);
    return () => {
      canvas.removeEventListener("pointerdown", down);
      canvas.removeEventListener("pointermove", move);
      canvas.removeEventListener("pointerup", up);
      canvas.removeEventListener("pointerleave", up);
    };
  }, [open, method]);

  const collectReference = (): string | null => {
    if (method === "Confirm") return null;
    if (method === "Signature") {
      const c = sigRef.current;
      if (!c) return null;
      const dataUrl = c.toDataURL("image/png");
      return `sig:${simpleHash(dataUrl).toString(16)}`;
    }
    return reference.trim() || null;
  };

  const canSubmit =
    scannedBy.trim().length > 0 &&
    !busy &&
    (method === "Confirm" || method === "Signature" || reference.trim().length > 0);

  return (
    <AnimatePresence>
      {open && itemId && (
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
              "fixed left-1/2 top-1/2 z-[90] w-[min(520px,calc(100vw-32px))] -translate-x-1/2 -translate-y-1/2",
              "rounded-2xl bg-[var(--color-surface)] shadow-[0_30px_80px_-20px_rgba(15,23,42,0.5)]",
            )}
            role="dialog"
            aria-modal="true"
          >
            <header className="flex items-start justify-between gap-3 border-b border-[var(--color-ink-100)] px-5 py-4 dark:border-white/[0.06]">
              <div className="flex items-center gap-2.5">
                <span className="inline-flex h-9 w-9 items-center justify-center rounded-full bg-[var(--color-success-soft)] text-[var(--color-success)]">
                  <CheckCircle2 className="h-4 w-4" strokeWidth={2.4} />
                </span>
                <div>
                  <h2 className="text-[15px] font-semibold text-[var(--color-ink-900)]">
                    Confirm POD
                  </h2>
                  {itemLabel && (
                    <p className="mt-0.5 font-mono text-[11px] text-[var(--color-ink-500)]">
                      {itemLabel}{orderRef ? ` · ${orderRef}` : ""}
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

            {/* Method tabs */}
            <div className="grid grid-cols-4 gap-1 border-b border-[var(--color-ink-100)] px-5 py-3 dark:border-white/[0.06]">
              <MethodTab active={method === "Confirm"}   onClick={() => setMethod("Confirm")}   icon={<CheckCircle2 className="h-3.5 w-3.5" strokeWidth={2.4} />} label="Confirm" />
              <MethodTab active={method === "Manual"}    onClick={() => setMethod("Manual")}    icon={<Keyboard    className="h-3.5 w-3.5" strokeWidth={2.4} />} label="Manual"  />
              <MethodTab active={method === "Barcode"}   onClick={() => setMethod("Barcode")}   icon={<Barcode     className="h-3.5 w-3.5" strokeWidth={2.4} />} label="Barcode" />
              <MethodTab active={method === "Signature"} onClick={() => setMethod("Signature")} icon={<PenLine    className="h-3.5 w-3.5" strokeWidth={2.4} />} label="Sign"    />
            </div>

            <form
              onSubmit={async (e) => {
                e.preventDefault();
                if (!canSubmit) return;
                await onConfirm({
                  scannedBy: scannedBy.trim(),
                  method,
                  reference: collectReference(),
                  scanType,
                });
              }}
              className="space-y-4 px-5 py-4"
            >
              <label className="block">
                <span className="text-[10.5px] font-semibold uppercase tracking-[0.06em] text-[var(--color-ink-500)]">
                  Scanned by
                </span>
                <input
                  type="text"
                  value={scannedBy}
                  onChange={(e) => setScannedBy(e.target.value)}
                  placeholder="e.g. ops-lead-01"
                  className="mt-1 w-full rounded-lg border border-[var(--color-ink-100)] bg-[var(--color-surface)] px-3 py-2 text-[13px] text-[var(--color-ink-900)] focus:border-[var(--color-brand-500)] focus:outline-none focus:ring-2 focus:ring-[var(--color-brand-500)]/20"
                  required
                />
              </label>

              {(method === "Manual" || method === "Barcode") && (
                <label className="block">
                  <span className="text-[10.5px] font-semibold uppercase tracking-[0.06em] text-[var(--color-ink-500)]">
                    {method === "Barcode" ? "Scanned code" : "Reference / SKU"}
                  </span>
                  <div className="relative mt-1">
                    {method === "Barcode" && (
                      <ScanLine className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-[var(--color-ink-400)]" strokeWidth={2.4} />
                    )}
                    <input
                      type="text"
                      autoFocus
                      value={reference}
                      onChange={(e) => setReference(e.target.value)}
                      placeholder={method === "Barcode" ? "Focus here, scan with USB scanner / camera" : "Type SKU or reference code"}
                      className={cn(
                        "w-full rounded-lg border border-[var(--color-ink-100)] bg-[var(--color-surface)] py-2 pr-3 text-[13px] text-[var(--color-ink-900)] focus:border-[var(--color-brand-500)] focus:outline-none focus:ring-2 focus:ring-[var(--color-brand-500)]/20",
                        method === "Barcode" ? "pl-9 font-mono" : "pl-3",
                      )}
                      required
                    />
                  </div>
                  {method === "Barcode" && (
                    <p className="mt-1 text-[10.5px] text-[var(--color-ink-400)]">
                      Compatible with USB barcode scanners (sends Enter on read).
                      Mobile: open camera in your browser&apos;s default scanner
                      app and copy into this field.
                    </p>
                  )}
                </label>
              )}

              {method === "Signature" && (
                <div>
                  <span className="text-[10.5px] font-semibold uppercase tracking-[0.06em] text-[var(--color-ink-500)]">
                    Recipient signature
                  </span>
                  <canvas
                    ref={sigRef}
                    width={460}
                    height={160}
                    className="mt-1 w-full rounded-lg border border-dashed border-[var(--color-ink-100)] bg-[var(--color-surface-soft)] touch-none cursor-crosshair dark:border-white/10 dark:bg-white/[0.04]"
                  />
                  <p className="mt-1 text-[10.5px] text-[var(--color-ink-400)]">
                    Draw signature with mouse / finger. A hash of the image is
                    stored as the POD reference — original image is not
                    persisted server-side.
                  </p>
                </div>
              )}

              {method === "Confirm" && (
                <p className="rounded-lg bg-[var(--color-success-soft)] px-3 py-2.5 text-[12px] leading-relaxed text-[var(--color-success)]">
                  No reference required. Your name + timestamp is recorded as
                  the proof. Use when items are visibly inspected and no
                  barcode / signature is available.
                </p>
              )}

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
                      ? "bg-[var(--color-success)] text-white hover:shadow-[0_14px_36px_-12px_rgba(16,185,129,0.5)]"
                      : "cursor-not-allowed bg-[var(--color-ink-100)] text-[var(--color-ink-400)] dark:bg-white/[0.04]",
                  )}
                >
                  <CheckCircle2 className={cn("h-3.5 w-3.5", busy && "animate-spin")} strokeWidth={2.4} />
                  Confirm POD
                </button>
              </div>
            </form>
          </motion.div>
        </>
      )}
    </AnimatePresence>
  );
}

function MethodTab({
  active, onClick, icon, label,
}: {
  active: boolean; onClick: () => void; icon: React.ReactNode; label: string;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        "inline-flex flex-col items-center gap-0.5 rounded-lg px-2 py-1.5 text-[10.5px] font-semibold uppercase tracking-[0.06em] transition-all",
        active
          ? "bg-[var(--color-brand-500)] text-white"
          : "text-[var(--color-ink-500)] hover:bg-[var(--color-ink-100)] dark:hover:bg-white/[0.04]",
      )}
    >
      {icon}
      {label}
    </button>
  );
}

// Tiny non-cryptographic hash so we can record signature evidence
// (browser-side) without depending on subtle.crypto and async APIs.
function simpleHash(s: string): number {
  let h = 5381;
  for (let i = 0; i < s.length; i++) h = ((h * 33) ^ s.charCodeAt(i)) >>> 0;
  return h;
}
