"use client";

import { Boxes, Loader2, X } from "lucide-react";
import { AnimatePresence, motion } from "motion/react";
import { useEffect, useState } from "react";
import {
  createCarrierTypeProfile,
  createLoadUnitProfile,
  type CarrierTypeProfile,
} from "@/lib/api/facility-profiles";
import { cn } from "@/lib/utils";

// ── shared modal shell ─────────────────────────────────────────────────────
function ModalShell({
  open,
  title,
  onClose,
  busy,
  error,
  onSubmit,
  submitLabel,
  canSubmit,
  children,
}: {
  open: boolean;
  title: string;
  onClose: () => void;
  busy: boolean;
  error: string | null;
  onSubmit: () => void;
  submitLabel: string;
  canSubmit: boolean;
  children: React.ReactNode;
}) {
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
                <span className="grid h-10 w-10 shrink-0 place-items-center rounded-full bg-[var(--color-pastel-mint)] text-[var(--color-brand-900)]">
                  <Boxes className="h-5 w-5" strokeWidth={2.2} />
                </span>
                <h2 className="font-display mt-1 flex-1 text-[1.2rem] font-semibold text-[var(--color-ink-900)]">
                  {title}
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
                {children}
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
                  onClick={onSubmit}
                  disabled={!canSubmit || busy}
                  whileHover={canSubmit && !busy ? { y: -1 } : {}}
                  whileTap={canSubmit && !busy ? { scale: 0.97 } : {}}
                  className={cn(
                    "inline-flex items-center gap-1.5 rounded-full px-4 py-2 text-[12px] font-semibold transition-all",
                    canSubmit && !busy
                      ? "bg-[var(--color-brand-900)] text-white hover:shadow-[0_14px_36px_-12px_rgba(15,23,42,0.6)] dark:bg-[var(--color-brand-500)]"
                      : "bg-[var(--color-ink-100)] text-[var(--color-ink-400)] cursor-not-allowed dark:bg-white/[0.04]",
                  )}
                >
                  {busy && <Loader2 className="h-3.5 w-3.5 animate-spin" strokeWidth={2.4} />}
                  {busy ? "Saving…" : submitLabel}
                </motion.button>
              </footer>
            </motion.div>
          </div>
        </>
      )}
    </AnimatePresence>
  );
}

// ── Carrier type profile ────────────────────────────────────────────────────
export function RegisterCarrierProfileDialog({
  open,
  onClose,
  onCreated,
}: {
  open: boolean;
  onClose: () => void;
  onCreated: () => void;
}) {
  const [code, setCode] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [amr, setAmr] = useState("");
  const [maxWeight, setMaxWeight] = useState("");
  const [maxSlots, setMaxSlots] = useState("");
  const [description, setDescription] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (open) {
      setCode("");
      setDisplayName("");
      setAmr("");
      setMaxWeight("");
      setMaxSlots("");
      setDescription("");
      setBusy(false);
      setError(null);
    }
  }, [open]);

  const canSubmit = code.trim() !== "" && displayName.trim() !== "" && amr.trim() !== "";

  const submit = async () => {
    if (!canSubmit) return;
    setBusy(true);
    setError(null);
    try {
      await createCarrierTypeProfile({
        code: code.trim(),
        displayName: displayName.trim(),
        aMRCapability: amr.trim(),
        maxWeightKg: maxWeight.trim() ? Number(maxWeight) : null,
        maxSlots: maxSlots.trim() ? Number(maxSlots) : null,
        description: description.trim() || null,
      });
      onCreated();
      onClose();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  };

  return (
    <ModalShell
      open={open}
      title="Register carrier type"
      onClose={onClose}
      busy={busy}
      error={error}
      onSubmit={() => void submit()}
      submitLabel="Register"
      canSubmit={canSubmit}
    >
      <div className="flex gap-3">
        <Field label="Code" className="flex-1">
          <input value={code} onChange={(e) => setCode(e.target.value)} placeholder="EUR-PALLET" className={inputCls} />
        </Field>
        <Field label="AMR capability" className="flex-1">
          <input value={amr} onChange={(e) => setAmr(e.target.value)} placeholder="FORK" className={inputCls} />
        </Field>
      </div>
      <Field label="Display name">
        <input value={displayName} onChange={(e) => setDisplayName(e.target.value)} placeholder="Euro pallet" className={inputCls} />
      </Field>
      <div className="flex gap-3">
        <Field label="Max weight (kg)" className="flex-1">
          <input type="number" value={maxWeight} onChange={(e) => setMaxWeight(e.target.value)} placeholder="optional" className={inputCls} />
        </Field>
        <Field label="Max slots" className="flex-1">
          <input type="number" value={maxSlots} onChange={(e) => setMaxSlots(e.target.value)} placeholder="optional" className={inputCls} />
        </Field>
      </div>
      <Field label="Description">
        <input value={description} onChange={(e) => setDescription(e.target.value)} placeholder="optional" className={inputCls} />
      </Field>
    </ModalShell>
  );
}

// ── Load unit profile ───────────────────────────────────────────────────────
export function RegisterLoadUnitProfileDialog({
  open,
  carriers,
  onClose,
  onCreated,
}: {
  open: boolean;
  carriers: CarrierTypeProfile[];
  onClose: () => void;
  onCreated: () => void;
}) {
  const [code, setCode] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [len, setLen] = useState("");
  const [wid, setWid] = useState("");
  const [hei, setHei] = useState("");
  const [maxGross, setMaxGross] = useState("");
  const [carrier, setCarrier] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (open) {
      setCode("");
      setDisplayName("");
      setLen("");
      setWid("");
      setHei("");
      setMaxGross("");
      setCarrier(carriers[0]?.code ?? "");
      setBusy(false);
      setError(null);
    }
  }, [open, carriers]);

  const canSubmit =
    code.trim() !== "" &&
    displayName.trim() !== "" &&
    carrier !== "" &&
    len.trim() !== "" &&
    wid.trim() !== "" &&
    hei.trim() !== "" &&
    maxGross.trim() !== "";

  const submit = async () => {
    if (!canSubmit) return;
    setBusy(true);
    setError(null);
    try {
      await createLoadUnitProfile({
        code: code.trim(),
        displayName: displayName.trim(),
        lengthMm: Number(len),
        widthMm: Number(wid),
        heightMm: Number(hei),
        maxGrossWeightKg: Number(maxGross),
        carrierTypeCode: carrier,
      });
      onCreated();
      onClose();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  };

  return (
    <ModalShell
      open={open}
      title="Register load unit"
      onClose={onClose}
      busy={busy}
      error={error}
      onSubmit={() => void submit()}
      submitLabel="Register"
      canSubmit={canSubmit}
    >
      <div className="flex gap-3">
        <Field label="Code" className="flex-1">
          <input value={code} onChange={(e) => setCode(e.target.value)} placeholder="BOX-S" className={inputCls} />
        </Field>
        <Field label="Carrier type" className="flex-1">
          <select value={carrier} onChange={(e) => setCarrier(e.target.value)} className={inputCls}>
            {carriers.length === 0 && <option value="">No carriers</option>}
            {carriers.map((c) => (
              <option key={c.code} value={c.code}>
                {c.code}
              </option>
            ))}
          </select>
        </Field>
      </div>
      <Field label="Display name">
        <input value={displayName} onChange={(e) => setDisplayName(e.target.value)} placeholder="Small box" className={inputCls} />
      </Field>
      <div className="flex gap-3">
        <Field label="Length (mm)" className="flex-1">
          <input type="number" value={len} onChange={(e) => setLen(e.target.value)} className={inputCls} />
        </Field>
        <Field label="Width (mm)" className="flex-1">
          <input type="number" value={wid} onChange={(e) => setWid(e.target.value)} className={inputCls} />
        </Field>
        <Field label="Height (mm)" className="flex-1">
          <input type="number" value={hei} onChange={(e) => setHei(e.target.value)} className={inputCls} />
        </Field>
      </div>
      <Field label="Max gross (kg)">
        <input type="number" value={maxGross} onChange={(e) => setMaxGross(e.target.value)} className={inputCls} />
      </Field>
    </ModalShell>
  );
}

function Field({
  label,
  className,
  children,
}: {
  label: string;
  className?: string;
  children: React.ReactNode;
}) {
  return (
    <label className={cn("flex flex-col gap-1", className)}>
      <span className="text-[10px] font-semibold uppercase tracking-[0.1em] text-[var(--color-ink-400)]">
        {label}
      </span>
      {children}
    </label>
  );
}

const inputCls =
  "h-9 rounded-md border border-white/70 bg-white/60 px-2.5 text-[12.5px] text-[var(--color-ink-900)] backdrop-blur-md focus:border-[var(--color-brand-500)]/30 focus:outline-none focus:ring-2 focus:ring-[var(--color-brand-500)]/40 dark:border-white/10 dark:bg-white/[0.05]";
