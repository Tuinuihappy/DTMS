"use client";

import { AnimatePresence, motion } from "motion/react";
import { OverlayBackdrop } from "@/components/primitives/overlay-backdrop";
import { Copy, Sparkles, Workflow, X } from "lucide-react";
import { useEffect, useState } from "react";
import { cn } from "@/lib/utils";
import {
  createActionTemplate,
  DEFAULT_ACTION_TYPE,
  formFromTemplate,
  updateActionTemplate,
  type ActionCategory,
  type ActionTemplateDto,
  type ActionTemplateFormPayload,
} from "@/lib/api/action-templates";

const EMPTY_FORM: ActionTemplateFormPayload = {
  actionName: "",
  actionCategory: "Std",
  actionType: DEFAULT_ACTION_TYPE,
  vendorActionId: null,
  param0: null,
  param1: null,
  paramStr: null,
};

export function ActionTemplateDialog({
  open,
  editing = null,
  duplicating = null,
  onClose,
  onSaved,
}: {
  open: boolean;
  editing?: ActionTemplateDto | null;
  // Source template for duplication. When set, the dialog runs in
  // create mode but pre-fills from this template and shows a banner.
  // Ignored if `editing` is also set (edit wins).
  duplicating?: ActionTemplateDto | null;
  onClose: () => void;
  onSaved: () => void;
}) {
  const isEdit = editing !== null;
  const isDuplicate = !isEdit && duplicating !== null;
  const [form, setForm] = useState<ActionTemplateFormPayload>(EMPTY_FORM);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!open) return;
    setError(null);
    if (editing) {
      setForm(formFromTemplate(editing));
    } else if (duplicating) {
      const base = formFromTemplate(duplicating);
      setForm({ ...base, actionName: `${base.actionName} (Copy)` });
    } else {
      setForm(EMPTY_FORM);
    }
  }, [open, editing, duplicating]);

  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) =>
      e.key === "Escape" && !submitting && onClose();
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [open, submitting, onClose]);

  const canSubmit = form.actionName.trim().length > 0;

  async function handleSubmit() {
    if (!canSubmit) return;
    setError(null);
    setSubmitting(true);
    try {
      const payload: ActionTemplateFormPayload = {
        ...form,
        actionName: form.actionName.trim(),
        actionType: form.actionType?.trim() || DEFAULT_ACTION_TYPE,
        paramStr: form.paramStr?.trim() ? form.paramStr.trim() : null,
      };
      if (editing) {
        await updateActionTemplate(editing.id, payload);
      } else {
        await createActionTemplate(payload);
      }
      onSaved();
      onClose();
    } catch (e) {
      setError(
        (e as Error).message ||
          (editing ? "Failed to update template." : "Failed to create template."),
      );
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <>
      {/* State-driven backdrop — see OverlayBackdrop for the stuck-exit
          rationale. Wrapper is pointer-events-none (panel re-enables)
          so a stranded exit can never swallow page clicks. */}
      <OverlayBackdrop
        open={open}
        onClick={() => !submitting && onClose()}
        className="z-40 bg-[var(--color-ink-900)]/50 backdrop-blur-md"
      />
      <AnimatePresence>
        {open && (
          <div
            key="create-action-template-dialog"
            className="pointer-events-none fixed inset-0 z-50 flex items-center justify-center p-4"
          >
            <motion.div
              initial={{ opacity: 0, scale: 0.94, y: 12 }}
              animate={{ opacity: 1, scale: 1, y: 0 }}
              exit={{ opacity: 0, scale: 0.96, y: 12, transition: { duration: 0.16 } }}
              transition={{ type: "spring", stiffness: 360, damping: 30 }}
              className={cn(
                "pointer-events-auto relative w-full max-w-xl overflow-hidden rounded-[var(--radius-xl)]",
                "glass-strong",
              )}
            >
              <div className="flex items-start justify-between gap-3 px-6 py-5">
                <div className="flex items-start gap-3">
                  <span className="grid h-10 w-10 place-items-center rounded-[14px] bg-gradient-to-br from-[var(--color-pastel-lavender)] to-[var(--color-pastel-sky)] text-[var(--color-brand-900)] shadow-[inset_0_1px_0_rgba(255,255,255,0.9),0_4px_10px_-4px_rgba(15,23,42,0.18)]">
                    {isDuplicate ? (
                      <Copy className="h-4 w-4" strokeWidth={2.1} />
                    ) : (
                      <Workflow className="h-4 w-4" strokeWidth={2.1} />
                    )}
                  </span>
                  <div>
                    <div className="text-[10.5px] font-semibold uppercase tracking-[0.12em] text-[var(--color-ink-400)]">
                      {isEdit
                        ? `Edit · ${editing!.actionName}`
                        : isDuplicate
                          ? `Duplicate · ${duplicating!.actionName}`
                          : "New action template"}
                    </div>
                    <h2 className="font-display mt-1 text-[1.4rem] font-semibold text-[var(--color-ink-900)]">
                      {isEdit
                        ? "Refine the block"
                        : isDuplicate
                          ? "Duplicate the block"
                          : "Compose a building block"}
                    </h2>
                  </div>
                </div>
                <button
                  type="button"
                  onClick={() => !submitting && onClose()}
                  className="rounded-full p-2 text-[var(--color-ink-500)] transition-colors hover:bg-white/40 hover:text-[var(--color-ink-900)] dark:hover:bg-white/10"
                  aria-label="Close"
                >
                  <X className="h-4 w-4" strokeWidth={2.4} />
                </button>
              </div>

              <div className="max-h-[65vh] overflow-y-auto px-6 py-5">
                <div className="space-y-5">
                  {isDuplicate && (
                    <div className="flex items-start gap-2.5 rounded-[var(--radius-sm)] border border-[var(--color-brand-500)]/25 bg-[var(--color-pastel-sky)]/50 px-3 py-2.5 text-[12px] text-[var(--color-brand-900)] dark:border-[var(--color-brand-500)]/30 dark:bg-[var(--color-brand-500)]/[0.08] dark:text-[var(--color-pastel-sky-ink)]">
                      <Copy className="mt-0.5 h-3.5 w-3.5 shrink-0" strokeWidth={2.2} />
                      <div className="min-w-0">
                        <div className="font-semibold">Duplicating an existing template</div>
                        <div className="mt-0.5 text-[11.5px] opacity-80">
                          Original:{" "}
                          <span className="font-mono font-semibold">
                            {duplicating!.actionName}
                          </span>
                          . A new template will be created — the original is unchanged.
                        </div>
                      </div>
                    </div>
                  )}
                  <Field
                    label="Action name"
                    required
                    hint="A short label your routing rules can reference."
                  >
                    <input
                      type="text"
                      value={form.actionName}
                      autoFocus={!isEdit}
                      onFocus={(e) => {
                        // Duplicate's pre-filled name is the first thing
                        // the user will edit — select it so they can just
                        // type a new name.
                        if (isDuplicate) e.currentTarget.select();
                      }}
                      onChange={(e) =>
                        setForm((f) => ({ ...f, actionName: e.target.value }))
                      }
                      placeholder="e.g. Pickup-at-Dock-A"
                      className={inputCls}
                    />
                  </Field>

                  <div>
                    <span className="mb-1.5 block text-[10.5px] font-semibold uppercase tracking-[0.1em] text-[var(--color-ink-500)]">
                      Category
                    </span>
                    <div className="flex gap-1.5">
                      {(
                        [
                          {
                            value: "Std" as ActionCategory,
                            label: "STD",
                            hint: "Standard step",
                          },
                          {
                            value: "Act" as ActionCategory,
                            label: "ACT",
                            hint: "Vendor action",
                          },
                        ] as const
                      ).map((opt) => {
                        const active = form.actionCategory === opt.value;
                        return (
                          <motion.button
                            key={opt.value}
                            type="button"
                            whileTap={{ scale: 0.96 }}
                            onClick={() =>
                              setForm((f) => ({ ...f, actionCategory: opt.value }))
                            }
                            className={cn(
                              "flex-1 rounded-[var(--radius-sm)] px-4 py-3 text-left transition-all",
                              active
                                ? "bg-[var(--color-brand-900)] text-white shadow-[0_4px_16px_-6px_rgba(15,23,42,0.55)] dark:bg-[var(--color-brand-500)]"
                                : "bg-white/55 text-[var(--color-ink-700)] hover:bg-white/80 dark:bg-white/[0.05] dark:hover:bg-white/[0.1]",
                            )}
                          >
                            <span className="font-display block text-[15px] font-semibold tracking-tight">
                              {opt.label}
                            </span>
                            <span
                              className={cn(
                                "mt-0.5 block text-[11px] font-medium",
                                active
                                  ? "text-white/70"
                                  : "text-[var(--color-ink-500)]",
                              )}
                            >
                              {opt.hint}
                            </span>
                          </motion.button>
                        );
                      })}
                    </div>
                  </div>

                  <div className="rounded-[var(--radius-sm)] border border-white/60 bg-white/40 p-4 dark:border-white/[0.06] dark:bg-white/[0.03]">
                    <div className="mb-3 flex items-center gap-2 text-[10.5px] font-semibold uppercase tracking-[0.1em] text-[var(--color-ink-500)]">
                      <Sparkles className="h-3 w-3" strokeWidth={2.2} />
                      Vendor parameters
                      <span className="text-[var(--color-ink-400)] normal-case tracking-normal">
                        · RIOT3-compatible
                      </span>
                    </div>
                    <div className="mb-3">
                      <Field
                        label="Action type"
                        compact
                        hint="Wire string sent as actionType to RIOT3."
                      >
                        <input
                          type="text"
                          value={form.actionType}
                          onChange={(e) =>
                            setForm((f) => ({
                              ...f,
                              actionType: e.target.value,
                            }))
                          }
                          placeholder={DEFAULT_ACTION_TYPE}
                          className={inputCls}
                        />
                      </Field>
                    </div>
                    <div className="grid grid-cols-2 gap-3">
                      <Field label="Vendor action ID" compact>
                        <input
                          type="number"
                          inputMode="numeric"
                          value={form.vendorActionId ?? ""}
                          onChange={(e) =>
                            setForm((f) => ({
                              ...f,
                              vendorActionId:
                                e.target.value === "" ? null : Number(e.target.value),
                            }))
                          }
                          placeholder="—"
                          className={inputCls}
                        />
                      </Field>
                      <Field label="Param 0" compact>
                        <input
                          type="number"
                          inputMode="numeric"
                          value={form.param0 ?? ""}
                          onChange={(e) =>
                            setForm((f) => ({
                              ...f,
                              param0:
                                e.target.value === "" ? null : Number(e.target.value),
                            }))
                          }
                          placeholder="—"
                          className={inputCls}
                        />
                      </Field>
                      <Field label="Param 1" compact>
                        <input
                          type="number"
                          inputMode="numeric"
                          value={form.param1 ?? ""}
                          onChange={(e) =>
                            setForm((f) => ({
                              ...f,
                              param1:
                                e.target.value === "" ? null : Number(e.target.value),
                            }))
                          }
                          placeholder="—"
                          className={inputCls}
                        />
                      </Field>
                      <Field label="Param string" compact>
                        <input
                          type="text"
                          value={form.paramStr ?? ""}
                          onChange={(e) =>
                            setForm((f) => ({
                              ...f,
                              paramStr: e.target.value === "" ? null : e.target.value,
                            }))
                          }
                          placeholder="—"
                          className={inputCls}
                        />
                      </Field>
                    </div>
                  </div>

                  {error && (
                    <motion.div
                      initial={{ opacity: 0, y: -4 }}
                      animate={{ opacity: 1, y: 0 }}
                      className="rounded-[var(--radius-sm)] border border-[var(--color-coral)]/30 bg-[var(--color-coral)]/10 px-3 py-2 text-[12.5px] text-[var(--color-coral)]"
                    >
                      {error}
                    </motion.div>
                  )}
                </div>
              </div>

              <div className="flex items-center justify-end gap-2 border-t border-white/60 bg-white/30 px-6 py-4 dark:border-white/[0.06] dark:bg-white/[0.02]">
                <button
                  type="button"
                  onClick={() => !submitting && onClose()}
                  disabled={submitting}
                  className="rounded-full px-4 py-2 text-[12.5px] font-semibold text-[var(--color-ink-600)] transition-colors hover:bg-white/40 disabled:opacity-50 dark:hover:bg-white/[0.05]"
                >
                  Cancel
                </button>
                <motion.button
                  type="button"
                  whileTap={{ scale: 0.97 }}
                  onClick={handleSubmit}
                  disabled={!canSubmit || submitting}
                  className={cn(
                    "rounded-full px-5 py-2 text-[12.5px] font-semibold transition-all",
                    "bg-[var(--color-brand-900)] text-white shadow-[0_4px_18px_-6px_rgba(15,23,42,0.45)] hover:shadow-[0_6px_22px_-6px_rgba(15,23,42,0.55)] dark:bg-[var(--color-brand-500)]",
                    "disabled:cursor-not-allowed disabled:opacity-50",
                  )}
                >
                  {submitting
                    ? isEdit
                      ? "Saving…"
                      : isDuplicate
                        ? "Creating copy…"
                        : "Creating…"
                    : isEdit
                      ? "Save changes"
                      : isDuplicate
                        ? "Create copy"
                        : "Create template"}
                </motion.button>
              </div>
            </motion.div>
          </div>
        )}
      </AnimatePresence>
    </>
  );
}

const inputCls = cn(
  "w-full rounded-lg bg-white/70 px-3 py-2 text-[13px] font-medium",
  "border border-white/80 backdrop-blur-md transition-all",
  "placeholder:text-[var(--color-ink-400)] text-[var(--color-ink-900)]",
  "focus:outline-none focus:ring-2 focus:ring-[var(--color-brand-500)]/40 focus:border-[var(--color-brand-500)]/30",
  "dark:bg-white/[0.05] dark:border-white/10",
);

function Field({
  label,
  required,
  compact,
  hint,
  children,
}: {
  label: string;
  required?: boolean;
  compact?: boolean;
  hint?: string;
  children: React.ReactNode;
}) {
  return (
    <label className="block">
      <span
        className={cn(
          "block font-semibold uppercase tracking-[0.1em] text-[var(--color-ink-500)]",
          compact ? "mb-1 text-[10px]" : "mb-1.5 text-[10.5px]",
        )}
      >
        {label}
        {required && <span className="ml-1 text-[var(--color-coral)]">*</span>}
      </span>
      {children}
      {hint && (
        <span className="mt-1 block text-[11px] text-[var(--color-ink-400)]">{hint}</span>
      )}
    </label>
  );
}
