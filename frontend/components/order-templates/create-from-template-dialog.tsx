"use client";

import { AnimatePresence, motion } from "motion/react";
import { OverlayBackdrop } from "@/components/primitives/overlay-backdrop";
import {
  AlertTriangle,
  ArrowLeft,
  ArrowRight,
  CheckCircle2,
  Eye,
  FileStack,
  Layers,
  Navigation,
  Rocket,
  Sparkles,
  Truck,
  Workflow,
  X,
} from "lucide-react";
import { useEffect, useMemo, useRef, useState } from "react";
import { cn } from "@/lib/utils";
import { formatRelative } from "@/lib/datetime";
import {
  createOrderFromTemplate,
  DispatchInProgressError,
  getLastDispatch,
  idempotencyKey as newIdempotencyKey,
  type CreateFromTemplateResult,
  type LastDispatchDto,
  type OrderTemplateDto,
  type ResolvedMission,
} from "@/lib/api/order-templates";

type Step = "form" | "preview" | "result";

type OverrideForm = {
  priority: string;
  appointVehicleKey: string;
  appointVehicleName: string;
  appointVehicleGroupKey: string;
  appointVehicleGroupName: string;
  appointQueueWaitArea: string;
  upperKey: string;
};

const EMPTY_FORM: OverrideForm = {
  priority: "",
  appointVehicleKey: "",
  appointVehicleName: "",
  appointVehicleGroupKey: "",
  appointVehicleGroupName: "",
  appointQueueWaitArea: "",
  upperKey: "",
};

function buildPayload(form: OverrideForm) {
  const num = form.priority.trim() === "" ? null : Number(form.priority);
  const priority = num != null && Number.isFinite(num) ? num : null;
  return {
    priority,
    appointVehicleKey: form.appointVehicleKey.trim() || null,
    appointVehicleName: form.appointVehicleName.trim() || null,
    appointVehicleGroupKey: form.appointVehicleGroupKey.trim() || null,
    appointVehicleGroupName: form.appointVehicleGroupName.trim() || null,
    appointQueueWaitArea: form.appointQueueWaitArea.trim() || null,
    upperKey: form.upperKey.trim() || null,
  };
}

export function CreateFromTemplateDialog({
  template,
  onClose,
  onSuccess,
}: {
  template: OrderTemplateDto | null;
  onClose: () => void;
  onSuccess: (msg: string) => void;
}) {
  const [step, setStep] = useState<Step>("form");
  const [form, setForm] = useState<OverrideForm>(EMPTY_FORM);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [preview, setPreview] = useState<CreateFromTemplateResult | null>(null);
  const [result, setResult] = useState<CreateFromTemplateResult | null>(null);
  // "Still confirming" is a state, not a failure — kept apart from `error`
  // so it renders calmly instead of as a red banner.
  const [pending, setPending] = useState<string | null>(null);
  const [lastDispatch, setLastDispatch] = useState<LastDispatchDto | null>(null);

  // Identity of ONE dispatch action. Held only while that request is in
  // flight so a double-click/retry reuses it, then released — the next click
  // is a new intent and must reach the robot. Dispatching the same template
  // repeatedly is normal, so this must never outlive a single request.
  const dispatchKeyRef = useRef<string | null>(null);
  const abortRef = useRef<AbortController | null>(null);

  const open = template !== null;

  useEffect(() => {
    if (!open) {
      // Abandon any in-flight request so its resolution can't write state
      // into a dialog the operator already dismissed.
      abortRef.current?.abort();
      abortRef.current = null;
      dispatchKeyRef.current = null;
      // Reset state shortly after close so the exit animation is clean.
      const t = setTimeout(() => {
        setStep("form");
        setForm(EMPTY_FORM);
        setBusy(false);
        setError(null);
        setPending(null);
        setPreview(null);
        setResult(null);
        setLastDispatch(null);
      }, 220);
      return () => clearTimeout(t);
    }
  }, [open]);

  // Show the previous attempt so an operator unsure whether their last click
  // landed can look instead of firing again. Purely informational — it never
  // disables the dispatch button.
  useEffect(() => {
    if (!open || !template) return;
    const ctrl = new AbortController();
    getLastDispatch(template.id, ctrl.signal)
      .then(setLastDispatch)
      .catch(() => {
        /* non-critical hint — stay silent on failure */
      });
    return () => ctrl.abort();
  }, [open, template]);

  const effectivePriority = useMemo(() => {
    if (!template) return null;
    const f = Number(form.priority);
    return form.priority.trim() === "" || !Number.isFinite(f) ? template.priority : f;
  }, [form.priority, template]);

  async function runPreview() {
    if (!template) return;
    const ctrl = new AbortController();
    abortRef.current?.abort();
    abortRef.current = ctrl;
    setBusy(true);
    setError(null);
    try {
      // Preview never reaches the vendor, so it needs no idempotency key.
      const res = await createOrderFromTemplate(
        template.id,
        { ...buildPayload(form), dryRun: true },
        { signal: ctrl.signal },
      );
      if (ctrl.signal.aborted) return;
      setPreview(res);
      setStep("preview");
    } catch (e) {
      if (ctrl.signal.aborted) return;
      setError((e as Error).message || "Preview failed.");
    } finally {
      if (!ctrl.signal.aborted) setBusy(false);
    }
  }

  async function runDispatch() {
    if (!template) return;
    const ctrl = new AbortController();
    abortRef.current?.abort();
    abortRef.current = ctrl;

    // One key per dispatch action: reused if this same request is retried,
    // released as soon as it settles. Keeping it any longer would swallow
    // the operator's next click, which is a legitimate second order.
    // Uses the shared helper because crypto.randomUUID is absent outside a
    // secure context (plain http on a LAN IP) and would throw here.
    dispatchKeyRef.current ??= newIdempotencyKey();
    const key = dispatchKeyRef.current;

    setBusy(true);
    setError(null);
    setPending(null);
    try {
      const res = await createOrderFromTemplate(
        template.id,
        { ...buildPayload(form), dryRun: false },
        { idempotencyKey: key, signal: ctrl.signal },
      );
      if (ctrl.signal.aborted) return;
      setResult(res);
      setStep("result");
      onSuccess(
        res.replayed
          ? `Already dispatched · ${res.upperKey}`
          : `Order dispatched · ${res.upperKey}`,
      );
      dispatchKeyRef.current = null;
    } catch (e) {
      if (ctrl.signal.aborted) return;
      if (e instanceof DispatchInProgressError) {
        // Not an error: an identical dispatch is still being confirmed. Keep
        // the key so a retry resolves the same attempt rather than creating
        // a second order.
        setPending(e.message);
      } else {
        setError((e as Error).message || "Dispatch failed.");
        // Vendor answered and refused — this attempt is closed, so release
        // the key and let a corrected retry go through as a new intent.
        dispatchKeyRef.current = null;
      }
    } finally {
      if (!ctrl.signal.aborted) setBusy(false);
    }
  }

  return (
    <>
      {/* State-driven backdrop — see OverlayBackdrop for the stuck-exit
          rationale. Wrapper is pointer-events-none (panel re-enables)
          so a stranded exit can never swallow page clicks. */}
      <OverlayBackdrop
        open={open && !!template}
        onClick={() => !busy && onClose()}
        className="z-50 bg-[var(--color-ink-900)]/55 backdrop-blur-md"
      />
      <AnimatePresence>
        {open && template && (
          <div
            key="create-from-template-dialog"
            className="pointer-events-none fixed inset-0 z-[55] flex items-center justify-center p-4"
          >
            <motion.div
              initial={{ opacity: 0, scale: 0.94, y: 12 }}
              animate={{ opacity: 1, scale: 1, y: 0 }}
              exit={{ opacity: 0, scale: 0.96, y: 12, transition: { duration: 0.18 } }}
              transition={{ type: "spring", stiffness: 320, damping: 30 }}
              className="pointer-events-auto glass-strong relative flex w-full max-w-2xl flex-col overflow-hidden rounded-[var(--radius-xl)]"
              style={{ maxHeight: "min(90vh, 760px)" }}
            >
              {/* Decorative header gradient. shrink-0 keeps the title and step
                  bar at full height when the body is tall — without it the
                  header is the item the browser squeezes, and its own
                  overflow-hidden then clips the title in half. */}
              <div className="relative shrink-0 overflow-hidden">
                <div className="absolute inset-0 bg-gradient-to-br from-[var(--color-pastel-sky)]/55 via-transparent to-[var(--color-pastel-lavender)]/55 pointer-events-none" />
                <div className="relative flex items-start justify-between gap-3 px-6 pt-6 pb-5">
                  <div className="flex items-start gap-3">
                    <span className="grid h-11 w-11 place-items-center rounded-[14px] bg-white/85 text-[var(--color-brand-900)] shadow-[inset_0_1px_0_rgba(255,255,255,0.9),0_6px_18px_-8px_rgba(15,23,42,0.25)] dark:bg-white/[0.08]">
                      <Rocket className="h-4 w-4" strokeWidth={2.2} />
                    </span>
                    <div className="min-w-0">
                      <div className="text-[10.5px] font-semibold uppercase tracking-[0.14em] text-[var(--color-ink-400)]">
                        Create order from template
                      </div>
                      <h2 className="font-display mt-1 truncate text-[1.3rem] font-semibold text-[var(--color-ink-900)]">
                        {template.name}
                      </h2>
                      <div className="mt-1 flex items-center gap-2 text-[11.5px] text-[var(--color-ink-500)]">
                        <FileStack className="h-3.5 w-3.5" strokeWidth={2.2} />
                        {(template.transportOrder?.missions?.length ?? 0)} missions ·
                        priority {template.priority}
                      </div>
                    </div>
                  </div>
                  <button
                    type="button"
                    onClick={() => !busy && onClose()}
                    className="cursor-pointer rounded-full p-2 text-[var(--color-ink-500)] transition-colors hover:bg-white/50 hover:text-[var(--color-ink-900)] dark:hover:bg-white/10"
                    aria-label="Close"
                  >
                    <X className="h-4 w-4" strokeWidth={2.4} />
                  </button>
                </div>

                {/* Step indicator */}
                <div className="relative px-6 pb-4">
                  <StepBar step={step} />
                </div>
              </div>

              {/* Body. min-h-0 is what actually lets this scroll: a flex item
                  defaults to min-height:auto, i.e. "never shrink below my
                  content", so without it the long mission list refuses to
                  shrink and the header gets squeezed instead. */}
              <div className="min-h-0 flex-1 overflow-y-auto px-6 py-5">
                <AnimatePresence mode="wait">
                  {step === "form" && (
                    <motion.div
                      key="form"
                      initial={{ opacity: 0, x: -8 }}
                      animate={{ opacity: 1, x: 0 }}
                      exit={{ opacity: 0, x: -8 }}
                      transition={{ duration: 0.22 }}
                      className="space-y-5"
                    >
                      <p className="rounded-[var(--radius-sm)] border border-[var(--color-brand-200)]/50 bg-[var(--color-pastel-sky)]/40 px-3.5 py-2.5 text-[12.5px] text-[var(--color-ink-700)] dark:border-white/[0.06] dark:bg-white/[0.04]">
                        <Sparkles className="mr-1.5 inline h-3.5 w-3.5 text-[var(--color-brand-500)]" strokeWidth={2.3} />
                        All fields below are <strong>optional overrides</strong>.
                        Leave blank to use the template defaults. Preview the
                        resolved order before sending it to RIOT3.
                      </p>

                      <SectionLabel
                        title="Order overrides"
                        hint="Applied on top of the stored template"
                      >
                        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
                          <Field
                            label="Priority"
                            placeholder={`default ${template.priority}`}
                            value={form.priority}
                            onChange={(v) => setForm({ ...form, priority: v })}
                            type="number"
                          />
                          <Field
                            label="Upper key"
                            placeholder="auto-generated"
                            value={form.upperKey}
                            onChange={(v) => setForm({ ...form, upperKey: v })}
                            mono
                          />
                        </div>
                      </SectionLabel>

                      <SectionLabel
                        title="Vehicle binding overrides"
                        hint="Target a specific vehicle, group, or wait area"
                      >
                        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
                          <Field
                            label="Vehicle key"
                            placeholder={template.appointVehicleKey ?? "—"}
                            value={form.appointVehicleKey}
                            onChange={(v) => setForm({ ...form, appointVehicleKey: v })}
                            icon={<Truck className="h-3.5 w-3.5" />}
                            mono
                          />
                          <Field
                            label="Vehicle name"
                            placeholder={template.appointVehicleName ?? "—"}
                            value={form.appointVehicleName}
                            onChange={(v) => setForm({ ...form, appointVehicleName: v })}
                          />
                          <Field
                            label="Group key"
                            placeholder={template.appointVehicleGroupKey ?? "—"}
                            value={form.appointVehicleGroupKey}
                            onChange={(v) => setForm({ ...form, appointVehicleGroupKey: v })}
                            icon={<Layers className="h-3.5 w-3.5" />}
                            mono
                          />
                          <Field
                            label="Group name"
                            placeholder={template.appointVehicleGroupName ?? "—"}
                            value={form.appointVehicleGroupName}
                            onChange={(v) => setForm({ ...form, appointVehicleGroupName: v })}
                          />
                          <Field
                            label="Queue wait area"
                            placeholder={template.appointQueueWaitArea ?? "—"}
                            value={form.appointQueueWaitArea}
                            onChange={(v) => setForm({ ...form, appointQueueWaitArea: v })}
                          />
                        </div>
                      </SectionLabel>

                      <div className="rounded-[var(--radius-sm)] bg-white/55 px-3.5 py-2.5 text-[12px] text-[var(--color-ink-600)] dark:bg-white/[0.03]">
                        <span className="font-semibold text-[var(--color-ink-800)]">
                          Resolved priority:
                        </span>{" "}
                        <span className="font-mono">{effectivePriority}</span>
                      </div>
                    </motion.div>
                  )}

                  {step === "preview" && preview && (
                    <motion.div
                      key="preview"
                      initial={{ opacity: 0, x: 8 }}
                      animate={{ opacity: 1, x: 0 }}
                      exit={{ opacity: 0, x: -8 }}
                      transition={{ duration: 0.22 }}
                      className="space-y-5"
                    >
                      <DryRunBanner upperKey={preview.upperKey} />
                      <ResolvedSummary preview={preview} />
                    </motion.div>
                  )}

                  {step === "result" && result && (
                    <motion.div
                      key="result"
                      initial={{ opacity: 0, scale: 0.96 }}
                      animate={{ opacity: 1, scale: 1 }}
                      transition={{ duration: 0.3, ease: [0.22, 1, 0.36, 1] }}
                      className="space-y-5"
                    >
                      <SuccessCard result={result} />
                      <ResolvedSummary preview={result} title="What was sent" />
                    </motion.div>
                  )}
                </AnimatePresence>

                {pending && (
                  <motion.div
                    initial={{ opacity: 0, y: -6 }}
                    animate={{ opacity: 1, y: 0 }}
                    className="mt-5 flex items-start gap-2 rounded-[var(--radius-sm)] border border-[var(--color-amber)]/40 bg-[var(--color-amber)]/10 px-3 py-2 text-[12.5px] text-[var(--color-amber)]"
                  >
                    <Sparkles className="mt-0.5 h-3.5 w-3.5 shrink-0" strokeWidth={2.2} />
                    <span>{pending}</span>
                  </motion.div>
                )}
                {error && (
                  <motion.div
                    initial={{ opacity: 0, y: -6 }}
                    animate={{ opacity: 1, y: 0 }}
                    className="mt-5 flex items-start gap-2 rounded-[var(--radius-sm)] border border-[var(--color-coral)]/40 bg-[var(--color-coral)]/10 px-3 py-2 text-[12.5px] text-[var(--color-coral)]"
                  >
                    <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0" strokeWidth={2.2} />
                    <span>{error}</span>
                  </motion.div>
                )}
                {step === "form" && lastDispatch && (
                  <p className="mt-4 text-[11.5px] text-[var(--color-ink-500)]">
                    Last dispatch{" "}
                    <span className="font-mono">{formatRelative(lastDispatch.createdAt)}</span>
                    {" · "}
                    <span
                      className={cn(
                        "font-semibold",
                        lastDispatch.status === "Succeeded" && "text-[var(--color-pastel-mint-ink)]",
                        lastDispatch.status === "Failed" && "text-[var(--color-coral)]",
                        lastDispatch.status === "InProgress" && "text-[var(--color-amber)]",
                      )}
                    >
                      {lastDispatch.status === "InProgress"
                        ? "outcome unknown"
                        : lastDispatch.status.toLowerCase()}
                    </span>
                    {" · "}
                    <span className="font-mono">{lastDispatch.upperKey}</span>
                  </p>
                )}
              </div>

              {/* Footer */}
              <div className="shrink-0 border-t border-white/60 bg-white/30 px-6 py-4 dark:border-white/[0.06] dark:bg-white/[0.02]">
                <Footer
                  step={step}
                  busy={busy}
                  onClose={onClose}
                  onBack={() => setStep("form")}
                  onPreview={runPreview}
                  onConfirm={runDispatch}
                />
              </div>
            </motion.div>
          </div>
        )}
      </AnimatePresence>
    </>
  );
}

// ── Sub-components ─────────────────────────────────────────────────────

function StepBar({ step }: { step: Step }) {
  const steps: { key: Step; label: string; icon: React.ReactNode }[] = [
    { key: "form", label: "Overrides", icon: <Sparkles className="h-3 w-3" /> },
    { key: "preview", label: "Dry-run preview", icon: <Eye className="h-3 w-3" /> },
    { key: "result", label: "Dispatched", icon: <CheckCircle2 className="h-3 w-3" /> },
  ];
  const activeIdx = steps.findIndex((s) => s.key === step);
  return (
    <ol className="flex items-center gap-1.5 text-[10.5px] font-semibold uppercase tracking-[0.1em]">
      {steps.map((s, i) => {
        const isDone = i < activeIdx;
        const isActive = i === activeIdx;
        return (
          <li key={s.key} className="flex items-center gap-1.5">
            <span
              className={cn(
                "inline-flex items-center gap-1 rounded-full px-2 py-0.5 transition-colors",
                isActive
                  ? "bg-[var(--color-brand-900)] text-white dark:bg-[var(--color-brand-500)]"
                  : isDone
                    ? "bg-[var(--color-success-soft)] text-[var(--color-success)]"
                    : "bg-white/55 text-[var(--color-ink-400)] dark:bg-white/[0.04]",
              )}
            >
              {s.icon}
              {s.label}
            </span>
            {i < steps.length - 1 && (
              <ArrowRight className="h-3 w-3 text-[var(--color-ink-300)]" strokeWidth={2.4} />
            )}
          </li>
        );
      })}
    </ol>
  );
}

function SectionLabel({
  title,
  hint,
  children,
}: {
  title: string;
  hint?: string;
  children: React.ReactNode;
}) {
  return (
    <section>
      <div className="mb-2 flex items-baseline justify-between gap-3">
        <h3 className="text-[10.5px] font-semibold uppercase tracking-[0.14em] text-[var(--color-ink-500)]">
          {title}
        </h3>
        {hint && (
          <span className="text-[11px] text-[var(--color-ink-400)]">{hint}</span>
        )}
      </div>
      {children}
    </section>
  );
}

function Field({
  label,
  value,
  onChange,
  placeholder,
  type = "text",
  icon,
  mono = false,
}: {
  label: string;
  value: string;
  onChange: (v: string) => void;
  placeholder?: string;
  type?: "text" | "number";
  icon?: React.ReactNode;
  mono?: boolean;
}) {
  return (
    <label className="block">
      <span className="block text-[10.5px] font-semibold uppercase tracking-[0.1em] text-[var(--color-ink-500)]">
        {label}
      </span>
      <div className="relative mt-1">
        {icon && (
          <span className="pointer-events-none absolute left-2.5 top-1/2 -translate-y-1/2 text-[var(--color-ink-400)]">
            {icon}
          </span>
        )}
        <input
          type={type}
          value={value}
          onChange={(e) => onChange(e.target.value)}
          placeholder={placeholder}
          className={cn(
            "w-full rounded-[var(--radius-sm)] border border-white/70 bg-white/65 px-3 py-2 text-[12.5px] text-[var(--color-ink-900)] placeholder:text-[var(--color-ink-400)] backdrop-blur transition-colors",
            "focus:border-[var(--color-brand-500)]/40 focus:bg-white focus:outline-none focus:ring-2 focus:ring-[var(--color-brand-500)]/20",
            "dark:border-white/[0.06] dark:bg-white/[0.04] dark:focus:bg-white/[0.08]",
            mono && "font-mono",
            icon && "pl-8",
          )}
        />
      </div>
    </label>
  );
}

function DryRunBanner({ upperKey }: { upperKey: string }) {
  return (
    <div className="flex items-center gap-3 rounded-[var(--radius-sm)] border border-[var(--color-brand-200)]/50 bg-[var(--color-pastel-sky)]/40 px-4 py-3 dark:border-white/[0.06] dark:bg-white/[0.04]">
      <span className="grid h-9 w-9 place-items-center rounded-[12px] bg-[var(--color-brand-900)] text-white dark:bg-[var(--color-brand-500)]">
        <Eye className="h-4 w-4" strokeWidth={2.3} />
      </span>
      <div className="min-w-0 flex-1">
        <div className="text-[10.5px] font-bold uppercase tracking-[0.12em] text-[var(--color-brand-900)] dark:text-[var(--color-brand-500)]">
          Dry-run preview
        </div>
        <div className="mt-0.5 text-[12.5px] text-[var(--color-ink-700)]">
          Nothing has been sent to RIOT3 yet. Review the resolved missions below,
          then confirm to dispatch.
        </div>
      </div>
      <div className="hidden sm:block text-right">
        <div className="text-[9.5px] font-semibold uppercase tracking-[0.1em] text-[var(--color-ink-400)]">
          Upper key
        </div>
        <div className="font-mono text-[11.5px] font-semibold text-[var(--color-ink-800)]">
          {upperKey}
        </div>
      </div>
    </div>
  );
}

function SuccessCard({ result }: { result: CreateFromTemplateResult }) {
  return (
    <motion.div
      initial={{ opacity: 0, y: 8 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.35, ease: [0.22, 1, 0.36, 1] }}
      className="relative overflow-hidden rounded-[var(--radius)] bg-gradient-to-br from-[var(--color-success-soft)] to-[var(--color-pastel-mint-tail)] px-5 py-4"
    >
      <div className="absolute -top-10 -right-10 h-32 w-32 rounded-full bg-white/40 blur-2xl" />
      <div className="relative flex items-start gap-3">
        <motion.span
          initial={{ scale: 0.6, rotate: -10 }}
          animate={{ scale: 1, rotate: 0 }}
          transition={{ type: "spring", stiffness: 380, damping: 22 }}
          className="grid h-11 w-11 place-items-center rounded-[14px] bg-[var(--color-success)] text-white shadow-[0_8px_22px_-8px_rgba(16,185,129,0.6)]"
        >
          <CheckCircle2 className="h-5 w-5" strokeWidth={2.3} />
        </motion.span>
        <div className="min-w-0 flex-1">
          <div className="text-[10.5px] font-bold uppercase tracking-[0.12em] text-[var(--color-success)]">
            Order created
          </div>
          <h3 className="font-display mt-0.5 text-[1.2rem] font-semibold text-[var(--color-ink-900)]">
            Sent to RIOT3
          </h3>
          <div className="mt-2 flex flex-wrap gap-3 text-[11.5px]">
            <KV label="Upper key" value={result.upperKey} />
            {result.riot3OrderKey && (
              <KV label="RIOT3 order" value={result.riot3OrderKey} />
            )}
          </div>
        </div>
      </div>
    </motion.div>
  );
}

function KV({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <div className="text-[9.5px] font-semibold uppercase tracking-[0.1em] text-[var(--color-ink-500)]">
        {label}
      </div>
      <div className="font-mono text-[12px] font-semibold text-[var(--color-ink-900)]">
        {value}
      </div>
    </div>
  );
}

function ResolvedSummary({
  preview,
  title = "Resolved missions",
}: {
  preview: CreateFromTemplateResult;
  title?: string;
}) {
  const order = preview.resolvedOrder;
  const missions = order.missions ?? [];
  return (
    <div className="space-y-3">
      <div className="grid grid-cols-1 gap-2.5 sm:grid-cols-3">
        <ResolvedKV label="Priority" value={order.priority ?? "—"} />
        <ResolvedKV label="Structure" value={order.structureType ?? "—"} />
        <ResolvedKV label="Missions" value={missions.length} />
        {(order.appointVehicleName || order.appointVehicleKey) && (
          <ResolvedKV
            label="Vehicle"
            value={order.appointVehicleName ?? order.appointVehicleKey ?? "—"}
          />
        )}
        {(order.appointVehicleGroupName || order.appointVehicleGroupKey) && (
          <ResolvedKV
            label="Group"
            value={
              order.appointVehicleGroupName ?? order.appointVehicleGroupKey ?? "—"
            }
          />
        )}
        {order.appointQueueWaitArea && (
          <ResolvedKV label="Wait area" value={order.appointQueueWaitArea} />
        )}
      </div>

      <section>
        <h4 className="mb-2 text-[10.5px] font-semibold uppercase tracking-[0.14em] text-[var(--color-ink-500)]">
          {title}
        </h4>
        <div className="space-y-1.5">
          {missions.length === 0 ? (
            <div className="rounded-[var(--radius-sm)] border border-dashed border-[var(--color-ink-200)] bg-white/40 px-3 py-3 text-center text-[12px] italic text-[var(--color-ink-500)] dark:border-white/[0.08] dark:bg-white/[0.02]">
              No resolved missions.
            </div>
          ) : (
            missions.map((m, i) => <ResolvedMissionRow key={i} mission={m} index={i} />)
          )}
        </div>
      </section>
    </div>
  );
}

function ResolvedKV({
  label,
  value,
}: {
  label: string;
  value: string | number | null;
}) {
  return (
    <div className="rounded-[var(--radius-sm)] border border-white/70 bg-white/55 px-3 py-2 dark:border-white/[0.06] dark:bg-white/[0.04]">
      <div className="text-[10px] font-semibold uppercase tracking-[0.12em] text-[var(--color-ink-400)]">
        {label}
      </div>
      <div className="mt-0.5 truncate font-mono text-[13px] font-semibold text-[var(--color-ink-900)]">
        {value ?? "—"}
      </div>
    </div>
  );
}

function ResolvedMissionRow({
  mission,
  index,
}: {
  mission: ResolvedMission;
  index: number;
}) {
  const isMove = String(mission.type).toUpperCase() === "MOVE";
  return (
    <motion.div
      initial={{ opacity: 0, x: 6 }}
      animate={{ opacity: 1, x: 0 }}
      transition={{ delay: index * 0.04, duration: 0.28 }}
      className="flex items-start gap-2.5 rounded-[var(--radius-sm)] border border-white/70 bg-white/55 px-3 py-2 dark:border-white/[0.06] dark:bg-white/[0.04]"
    >
      <span
        className={cn(
          "grid h-7 w-7 shrink-0 place-items-center rounded-full text-white",
          isMove
            ? "bg-gradient-to-br from-[var(--color-pastel-mint-ink)] to-[#0e6a4d]"
            : "bg-gradient-to-br from-[var(--color-pastel-peach-ink)] to-[#a8421d]",
        )}
      >
        {isMove ? (
          <Navigation className="h-3.5 w-3.5" strokeWidth={2.4} />
        ) : (
          <Workflow className="h-3.5 w-3.5" strokeWidth={2.4} />
        )}
      </span>
      <div className="min-w-0 flex-1">
        <div className="flex flex-wrap items-center gap-1.5">
          <span className="font-mono text-[10px] font-bold uppercase tracking-[0.1em] text-[var(--color-ink-400)]">
            #{index + 1}
          </span>
          <span
            className={cn(
              "rounded-full px-1.5 py-0.5 text-[10px] font-bold tracking-[0.08em]",
              isMove
                ? "bg-[var(--color-pastel-mint)] text-[var(--color-pastel-mint-ink)]"
                : "bg-[var(--color-pastel-peach)] text-[var(--color-pastel-peach-ink)]",
            )}
          >
            {String(mission.type).toUpperCase()}
          </span>
          {mission.actionType && (
            <span className="font-mono text-[11px] font-semibold text-[var(--color-ink-800)]">
              {mission.actionType}
            </span>
          )}
          {mission.stationId != null && (
            <span className="rounded-full bg-white/55 px-1.5 py-0.5 font-mono text-[10.5px] text-[var(--color-ink-700)] dark:bg-white/[0.06]">
              station {mission.stationId}
            </span>
          )}
          {mission.mapId != null && (
            <span className="rounded-full bg-white/55 px-1.5 py-0.5 font-mono text-[10.5px] text-[var(--color-ink-700)] dark:bg-white/[0.06]">
              map {mission.mapId}
            </span>
          )}
        </div>
        {mission.actionParameters && mission.actionParameters.length > 0 && (
          <div className="mt-1.5 flex flex-wrap gap-1">
            {mission.actionParameters.map((p, i) => (
              <span
                key={`${p.key}-${i}`}
                className="inline-flex items-center gap-1 rounded-[6px] bg-[var(--color-ink-100)]/60 px-1.5 py-0.5 font-mono text-[10px] dark:bg-white/[0.06]"
              >
                <span className="text-[var(--color-ink-500)]">{p.key}</span>
                <span className="text-[var(--color-ink-300)]">=</span>
                <span className="font-semibold text-[var(--color-ink-800)]">
                  {p.value == null || p.value === "" ? "—" : String(p.value)}
                </span>
              </span>
            ))}
          </div>
        )}
      </div>
    </motion.div>
  );
}

function Footer({
  step,
  busy,
  onClose,
  onBack,
  onPreview,
  onConfirm,
}: {
  step: Step;
  busy: boolean;
  onClose: () => void;
  onBack: () => void;
  onPreview: () => void;
  onConfirm: () => void;
}) {
  if (step === "form") {
    return (
      <div className="flex flex-wrap items-center justify-end gap-2">
        <FooterBtn variant="ghost" onClick={onClose} disabled={busy}>
          Cancel
        </FooterBtn>
        <FooterBtn
          variant="primary"
          onClick={onPreview}
          disabled={busy}
          icon={<Eye className="h-3.5 w-3.5" />}
        >
          {busy ? "Resolving…" : "Preview dry-run"}
        </FooterBtn>
      </div>
    );
  }
  if (step === "preview") {
    return (
      <div className="flex flex-wrap items-center justify-between gap-2">
        <FooterBtn
          variant="ghost"
          onClick={onBack}
          disabled={busy}
          icon={<ArrowLeft className="h-3.5 w-3.5" />}
        >
          Edit overrides
        </FooterBtn>
        <div className="flex flex-wrap items-center gap-2">
          <FooterBtn variant="ghost" onClick={onClose} disabled={busy}>
            Cancel
          </FooterBtn>
          <FooterBtn
            variant="confirm"
            onClick={onConfirm}
            disabled={busy}
            icon={<Rocket className="h-3.5 w-3.5" />}
          >
            {busy ? "Dispatching…" : "Confirm & dispatch"}
          </FooterBtn>
        </div>
      </div>
    );
  }
  return (
    <div className="flex flex-wrap items-center justify-end gap-2">
      <FooterBtn variant="primary" onClick={onClose}>
        Done
      </FooterBtn>
    </div>
  );
}

const FOOTER_STYLES = {
  primary:
    "bg-[var(--color-brand-900)] text-white shadow-[0_4px_16px_-6px_rgba(15,23,42,0.45)] hover:shadow-[0_6px_20px_-6px_rgba(15,23,42,0.55)] dark:bg-[var(--color-brand-500)]",
  confirm:
    "bg-[var(--color-success)] text-white shadow-[0_6px_22px_-8px_rgba(16,185,129,0.5)] hover:shadow-[0_8px_28px_-8px_rgba(16,185,129,0.6)]",
  ghost:
    "text-[var(--color-ink-600)] hover:bg-white/55 dark:hover:bg-white/[0.06]",
} as const;

function FooterBtn({
  variant,
  onClick,
  disabled,
  children,
  icon,
}: {
  variant: keyof typeof FOOTER_STYLES;
  onClick: () => void;
  disabled?: boolean;
  children: React.ReactNode;
  icon?: React.ReactNode;
}) {
  return (
    <motion.button
      type="button"
      whileTap={!disabled ? { scale: 0.96 } : undefined}
      onClick={onClick}
      disabled={disabled}
      className={cn(
        "inline-flex cursor-pointer items-center gap-1.5 rounded-full px-4 py-2 text-[12.5px] font-semibold transition-all disabled:cursor-not-allowed disabled:opacity-50",
        FOOTER_STYLES[variant],
      )}
    >
      {icon}
      {children}
    </motion.button>
  );
}
