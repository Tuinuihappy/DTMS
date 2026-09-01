"use client";

import { Loader2, PackagePlus, X } from "lucide-react";
import { AnimatePresence, motion } from "motion/react";
import { useEffect, useState } from "react";
import { OverlayBackdrop } from "@/components/primitives/overlay-backdrop";
import { createCarrier, updateCarrier, type Carrier } from "@/lib/api/fleet-carriers";
import type { CarrierTypeProfile } from "@/lib/api/facility-profiles";
import { cn } from "@/lib/utils";

const toDateInput = (iso: string | null) => (iso ? iso.slice(0, 10) : "");

/**
 * Register or edit. One form for both because the fields are identical apart
 * from the code, which is immutable once assigned — carrier codes identify one
 * physical cart forever (ADR-019), so a rename would make history ambiguous
 * exactly the way reuse would.
 */
export function CarrierFormDialog({
  open,
  carrier,
  carrierTypes,
  onClose,
  onSaved,
}: {
  open: boolean;
  /** null → register mode; a carrier → edit mode with the code locked. */
  carrier: Carrier | null;
  carrierTypes: CarrierTypeProfile[];
  onClose: () => void;
  onSaved: () => void;
}) {
  const isEdit = carrier !== null;

  const [code, setCode] = useState("");
  const [carrierTypeCode, setCarrierTypeCode] = useState("");
  const [barcode, setBarcode] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [location, setLocation] = useState("");
  const [commissionedAt, setCommissionedAt] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (open) {
      setCode(carrier?.carrierCode ?? "");
      setCarrierTypeCode(carrier?.carrierTypeCode ?? carrierTypes[0]?.code ?? "");
      setBarcode(carrier?.barcode ?? "");
      setDisplayName(carrier?.displayName ?? "");
      setLocation(carrier?.currentLocationCode ?? "");
      setCommissionedAt(toDateInput(carrier?.commissionedAt ?? null));
      setBusy(false);
      setError(null);
    }
  }, [open, carrier, carrierTypes]);

  // Mirrors the server-side rule. The code becomes a URL path segment, so a
  // space or slash would break the detail route — better to say so while the
  // user is typing than to fail the request.
  const codeOk = code.trim() === "" || /^[A-Za-z0-9][A-Za-z0-9._-]{0,49}$/.test(code.trim());
  const canSubmit = code.trim() !== "" && codeOk && carrierTypeCode !== "";

  const submit = async () => {
    if (!canSubmit) return;
    setBusy(true);
    setError(null);
    const commissioned = commissionedAt ? new Date(commissionedAt).toISOString() : null;
    try {
      if (isEdit) {
        // Location is not part of the edit payload — it has its own action so
        // that moving a carrier always stamps LastSeenAt.
        await updateCarrier(carrier.carrierCode, {
          carrierTypeCode,
          barcode: barcode.trim() || null,
          displayName: displayName.trim() || null,
          commissionedAt: commissioned,
        });
      } else {
        await createCarrier({
          carrierCode: code.trim(),
          carrierTypeCode,
          barcode: barcode.trim() || null,
          displayName: displayName.trim() || null,
          currentLocationCode: location.trim() || null,
          commissionedAt: commissioned,
        });
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
    <>
      <OverlayBackdrop
        open={open}
        onClick={() => !busy && onClose()}
        className="z-40 bg-[var(--color-ink-900)]/55 backdrop-blur-md"
      />
      <AnimatePresence>
        {open && (
          <div
            key="register-carrier-dialog"
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
                <span className="grid h-10 w-10 shrink-0 place-items-center rounded-full bg-[var(--color-pastel-mint)] text-[var(--color-brand-900)]">
                  <PackagePlus className="h-5 w-5" strokeWidth={2.2} />
                </span>
                <h2 className="font-display mt-1 flex-1 text-[1.2rem] font-semibold text-[var(--color-ink-900)]">
                  {isEdit ? `Edit ${carrier.carrierCode}` : "Register carrier"}
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
                <div className="flex gap-3">
                  <Field label="Carrier code" className="flex-1">
                    <input
                      value={code}
                      onChange={(e) => setCode(e.target.value)}
                      placeholder="CART-0001"
                      disabled={isEdit}
                      className={cn(
                        inputCls,
                        !codeOk && "border-[var(--color-coral)]",
                        isEdit && "cursor-not-allowed opacity-60",
                      )}
                    />
                    {isEdit ? (
                      <span className="mt-1 text-[10.5px] text-[var(--color-ink-500)]">
                        Codes are permanent — they identify this carrier in every record.
                      </span>
                    ) : (
                      !codeOk && (
                        <span className="mt-1 text-[10.5px] text-[var(--color-coral)]">
                          Letters, digits, dot, underscore or hyphen only — it becomes part of the URL.
                        </span>
                      )
                    )}
                  </Field>
                  <Field label="Carrier type" className="flex-1">
                    <select
                      value={carrierTypeCode}
                      onChange={(e) => setCarrierTypeCode(e.target.value)}
                      className={inputCls}
                    >
                      {carrierTypes.length === 0 && <option value="">No carrier types</option>}
                      {carrierTypes.map((t) => (
                        <option key={t.code} value={t.code}>
                          {t.code}
                        </option>
                      ))}
                    </select>
                  </Field>
                </div>

                <Field label="Display name">
                  <input
                    value={displayName}
                    onChange={(e) => setDisplayName(e.target.value)}
                    placeholder="optional"
                    className={inputCls}
                  />
                </Field>

                <div className="flex gap-3">
                  <Field label="Barcode" className="flex-1">
                    <input
                      value={barcode}
                      onChange={(e) => setBarcode(e.target.value)}
                      placeholder="optional, if different from code"
                      className={inputCls}
                    />
                  </Field>
                  {/* Only on register. Editing a location goes through the
                      move action so LastSeenAt is always stamped with it. */}
                  {!isEdit && (
                    <Field label="Location" className="flex-1">
                      <input
                        value={location}
                        onChange={(e) => setLocation(e.target.value)}
                        placeholder="optional"
                        className={inputCls}
                      />
                    </Field>
                  )}
                </div>

                <Field label="In service since">
                  <input
                    type="date"
                    value={commissionedAt}
                    onChange={(e) => setCommissionedAt(e.target.value)}
                    className={inputCls}
                  />
                  <span className="mt-1 text-[10.5px] text-[var(--color-ink-500)]">
                    When the carrier physically entered service — may be backdated.
                  </span>
                </Field>

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
                  onClick={() => void submit()}
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
                  {busy ? "Saving…" : isEdit ? "Save" : "Register"}
                </motion.button>
              </footer>
            </motion.div>
          </div>
        )}
      </AnimatePresence>
    </>
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
