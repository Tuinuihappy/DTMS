"use client";

import { Boxes, Loader2, X } from "lucide-react";
import { motion } from "motion/react";
import { ModalLayer } from "@/components/primitives/modal-layer";
import { useEffect, useState } from "react";
import {
  createCarrierTypeProfile,
  updateCarrierTypeProfile,
  type CarrierTypeProfile,
} from "@/lib/api/fleet-carrier-types";
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
    <>
      <ModalLayer
        open={open}
        onClose={onClose}
        dismissible={!busy}
        backdropClassName="bg-[var(--color-ink-900)]/55 backdrop-blur-md"
      >
        {({ panelProps, titleId, panelStyle }) => (
          <div
            style={panelStyle}
            key="carrier-type-form-dialog"
            className="pointer-events-none fixed inset-0 flex items-center justify-center p-4"
          >
            <motion.div
              {...panelProps}
              initial={{ opacity: 0, scale: 0.94, y: 12 }}
              animate={{ opacity: 1, scale: 1, y: 0 }}
              exit={{ opacity: 0, scale: 0.96, y: 12, transition: { duration: 0.16 } }}
              transition={{ type: "spring", stiffness: 360, damping: 30 }}
              className="pointer-events-auto relative w-full max-w-md overflow-hidden rounded-[var(--radius-xl)] glass-strong outline-none"
            >
              <header className="flex items-start gap-3 px-6 pt-5">
                <span className="grid h-10 w-10 shrink-0 place-items-center rounded-full bg-[var(--color-pastel-mint)] text-[var(--color-brand-900)]">
                  <Boxes className="h-5 w-5" strokeWidth={2.2} />
                </span>
                <h2 id={titleId} className="font-display mt-1 flex-1 text-[1.2rem] font-semibold text-[var(--color-ink-900)]">
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
        )}
      </ModalLayer>
    </>
  );
}

// ── Carrier type ────────────────────────────────────────────────────────────
/**
 * Register or edit. One form for both because the fields are identical apart
 * from the code, which is locked once assigned: it addresses the row and is how
 * every carrier payload names its type, so a rename would silently invalidate
 * anything holding the old one.
 */
export function CarrierTypeFormDialog({
  open,
  carrierType,
  onClose,
  onSaved,
}: {
  open: boolean;
  /** null → register mode; a profile → edit mode with the code locked. */
  carrierType: CarrierTypeProfile | null;
  onClose: () => void;
  onSaved: () => void;
}) {
  const isEdit = carrierType !== null;

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
      setCode(carrierType?.code ?? "");
      setDisplayName(carrierType?.displayName ?? "");
      setAmr(carrierType?.amrCapability ?? "");
      setMaxWeight(carrierType?.maxWeightKg != null ? String(carrierType.maxWeightKg) : "");
      setMaxSlots(carrierType?.maxSlots != null ? String(carrierType.maxSlots) : "");
      setDescription(carrierType?.description ?? "");
      setBusy(false);
      setError(null);
    }
  }, [open, carrierType]);

  // Mirrors the server-side rule. The code becomes a URL path segment, so a
  // space or slash would break the detail route — better to say so while the
  // user is typing than to fail the request.
  const codeOk = code.trim() === "" || /^[A-Za-z0-9][A-Za-z0-9._-]{0,49}$/.test(code.trim());
  const canSubmit =
    code.trim() !== "" && codeOk && displayName.trim() !== "" && amr.trim() !== "";

  const submit = async () => {
    if (!canSubmit) return;
    setBusy(true);
    setError(null);
    const fields = {
      displayName: displayName.trim(),
      amrCapability: amr.trim(),
      maxWeightKg: maxWeight.trim() ? Number(maxWeight) : null,
      maxSlots: maxSlots.trim() ? Number(maxSlots) : null,
      description: description.trim() || null,
    };
    try {
      if (isEdit) {
        await updateCarrierTypeProfile(carrierType.code, fields);
      } else {
        await createCarrierTypeProfile({ code: code.trim(), ...fields });
      }
      onSaved();
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
      title={isEdit ? `Edit ${carrierType.code}` : "Register carrier type"}
      onClose={onClose}
      busy={busy}
      error={error}
      onSubmit={() => void submit()}
      submitLabel={isEdit ? "Save" : "Register"}
      canSubmit={canSubmit}
    >
      <div className="flex gap-3">
        <Field label="Code" className="flex-1">
          <input
            value={code}
            onChange={(e) => setCode(e.target.value)}
            placeholder="EUR-PALLET"
            disabled={isEdit}
            className={cn(
              inputCls,
              !codeOk && "border-[var(--color-coral)]",
              isEdit && "cursor-not-allowed opacity-60",
            )}
          />
          {isEdit ? (
            <span className="mt-1 text-[10.5px] text-[var(--color-ink-500)]">
              Codes are permanent — every carrier names its type by this.
            </span>
          ) : (
            !codeOk && (
              <span className="mt-1 text-[10.5px] text-[var(--color-coral)]">
                Letters, digits, dot, underscore or hyphen only — it becomes part of the URL.
              </span>
            )
          )}
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
