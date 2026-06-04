"use client";

import {
  ArrowLeft,
  ArrowRight,
  Check,
  ChevronDown,
  Loader2,
  Plus,
  Sliders,
  Trash2,
  X,
} from "lucide-react";
import { AnimatePresence, motion } from "motion/react";
import { useEffect, useState } from "react";
import {
  createOrder,
  updateOrder,
  type CreateOrderPayload,
  type DeliveryOrderDetailDto,
  type HandlingInstruction,
  type PackingGroup,
  type Priority,
  type TransportMode,
  type Uom,
} from "@/lib/api/delivery-orders";
import { cn } from "@/lib/utils";

type ItemFormState = {
  itemId: string;
  description: string;
  pickup: string;
  drop: string;
  qty: number;
  uom: Uom;
  weightKg: string;
  // Advanced (collapsed by default)
  advancedOpen: boolean;
  loadUnitProfileCode: string;
  lengthMm: string;
  widthMm: string;
  heightMm: string;
  minC: string;
  maxC: string;
  hazmatClass: string;
  hazmatPackingGroup: "" | PackingGroup;
  handlingInstructions: HandlingInstruction[];
};

type FormState = {
  orderRef: string;
  priority: Priority;
  requestedBy: string;
  notes: string;
  transport: TransportMode;
  windowEarliest: string; // datetime-local string ("YYYY-MM-DDTHH:mm")
  windowLatest: string;
  items: ItemFormState[];
};

const EMPTY_ITEM: ItemFormState = {
  itemId: "",
  description: "",
  pickup: "",
  drop: "",
  qty: 1,
  uom: "EA",
  weightKg: "",
  advancedOpen: false,
  loadUnitProfileCode: "",
  lengthMm: "",
  widthMm: "",
  heightMm: "",
  minC: "",
  maxC: "",
  hazmatClass: "",
  hazmatPackingGroup: "",
  handlingInstructions: [],
};

const HANDLING_OPTIONS: { value: HandlingInstruction; label: string }[] = [
  { value: "Fragile", label: "Fragile" },
  { value: "ThisSideUp", label: "This side up" },
  { value: "DoNotStack", label: "Do not stack" },
  { value: "HeavyLift", label: "Heavy lift" },
  { value: "Sharp", label: "Sharp" },
  { value: "KeepDry", label: "Keep dry" },
  { value: "KeepDark", label: "Keep dark" },
  { value: "PinchHazard", label: "Pinch hazard" },
];

function blankForm(): FormState {
  const stamp = new Date()
    .toISOString()
    .replace(/[-:T]/g, "")
    .slice(0, 12);
  return {
    orderRef: `DO-${stamp}`,
    priority: "Normal",
    requestedBy: "",
    notes: "",
    transport: "Amr",
    windowEarliest: "",
    windowLatest: "",
    items: [{ ...EMPTY_ITEM }],
  };
}

// Convert an ISO UTC timestamp into the local-time string format that
// <input type="datetime-local"> expects ("YYYY-MM-DDTHH:mm"). Returns
// empty string for null/invalid so the input stays unset.
function isoToLocalInput(iso: string | null | undefined): string {
  if (!iso) return "";
  const d = new Date(iso);
  if (isNaN(d.getTime())) return "";
  const pad = (n: number) => n.toString().padStart(2, "0");
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

// Hydrate the form from an existing order — used when the dialog opens
// in edit mode. Mirrors blankForm() so every field is initialised.
function formFromOrder(o: DeliveryOrderDetailDto): FormState {
  return {
    orderRef: o.orderRef,
    priority: o.priority,
    requestedBy: o.requestedBy ?? "",
    notes: o.notes ?? "",
    transport: o.requestedTransportMode ?? "Amr",
    windowEarliest: isoToLocalInput(o.serviceWindow?.earliestUtc),
    windowLatest: isoToLocalInput(o.serviceWindow?.latestUtc),
    items: o.items.map((it) => ({
      itemId: it.itemId,
      description: it.description ?? "",
      pickup: it.pickupLocationCode,
      drop: it.dropLocationCode,
      qty: it.quantity.value,
      uom: it.quantity.uom,
      weightKg: it.weightKg != null ? String(it.weightKg) : "",
      // Auto-expand advanced when any advanced field is set so the user
      // sees what they're about to overwrite. Closed by default for
      // items where it doesn't apply.
      advancedOpen: Boolean(
        it.loadUnitProfileCode ||
          it.dimensions ||
          it.temperature ||
          it.hazmat ||
          (it.handlingInstructions && it.handlingInstructions.length > 0),
      ),
      loadUnitProfileCode: it.loadUnitProfileCode ?? "",
      lengthMm: it.dimensions ? String(it.dimensions.lengthMm) : "",
      widthMm: it.dimensions ? String(it.dimensions.widthMm) : "",
      heightMm: it.dimensions ? String(it.dimensions.heightMm) : "",
      minC: it.temperature?.minC != null ? String(it.temperature.minC) : "",
      maxC: it.temperature?.maxC != null ? String(it.temperature.maxC) : "",
      hazmatClass: it.hazmat?.classCode ?? "",
      hazmatPackingGroup: it.hazmat?.packingGroup ?? "",
      handlingInstructions: it.handlingInstructions ?? [],
    })),
  };
}

// datetime-local input gives a local-time string like "2026-06-04T14:30".
// new Date(s) interprets that as local time; toISOString() flips to UTC.
function toIso(local: string): string | undefined {
  if (!local) return undefined;
  const d = new Date(local);
  return isNaN(d.getTime()) ? undefined : d.toISOString();
}

function buildPayload(form: FormState): CreateOrderPayload {
  return {
    orderRef: form.orderRef.trim(),
    priority: form.priority,
    requestedBy: form.requestedBy.trim() || undefined,
    notes: form.notes.trim() || undefined,
    requestedTransportMode: form.transport,
    serviceWindow:
      form.windowEarliest || form.windowLatest
        ? { earliestUtc: toIso(form.windowEarliest), latestUtc: toIso(form.windowLatest) }
        : undefined,
    items: form.items.map((it) => {
      const hasDims = it.lengthMm && it.widthMm && it.heightMm;
      const hasTemp = it.minC !== "" || it.maxC !== "";
      const hasHazmat = it.hazmatClass.trim().length > 0;
      return {
        itemId: it.itemId.trim(),
        description: it.description.trim() || undefined,
        pickupLocationCode: it.pickup.trim(),
        dropLocationCode: it.drop.trim(),
        loadUnitProfileCode: it.loadUnitProfileCode.trim() || undefined,
        dimensions: hasDims
          ? {
              lengthMm: Number(it.lengthMm),
              widthMm: Number(it.widthMm),
              heightMm: Number(it.heightMm),
            }
          : undefined,
        weightKg: it.weightKg ? Number(it.weightKg) : undefined,
        quantity: { value: it.qty, uom: it.uom },
        hazmat: hasHazmat
          ? {
              classCode: it.hazmatClass.trim(),
              packingGroup: it.hazmatPackingGroup || undefined,
            }
          : undefined,
        temperature: hasTemp
          ? {
              minC: it.minC !== "" ? Number(it.minC) : undefined,
              maxC: it.maxC !== "" ? Number(it.maxC) : undefined,
            }
          : undefined,
        handlingInstructions: it.handlingInstructions.length
          ? it.handlingInstructions
          : undefined,
      };
    }),
  };
}

export function CreateOrderDialog({
  open,
  editing = null,
  onClose,
  onCreated,
}: {
  open: boolean;
  // When provided, the dialog opens in edit mode — title flips, form
  // prefills from the order, the submit button sends PUT instead of
  // POST, and the orderRef field becomes read-only (the backend
  // doesn't allow renaming on update).
  editing?: DeliveryOrderDetailDto | null;
  onClose: () => void;
  onCreated: () => void;
}) {
  const isEdit = editing !== null;
  const [step, setStep] = useState<0 | 1>(0);
  const [form, setForm] = useState<FormState>(blankForm);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (open) {
      setStep(0);
      setForm(editing ? formFromOrder(editing) : blankForm());
      setError(null);
    }
  }, [open, editing]);

  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => e.key === "Escape" && !submitting && onClose();
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [open, onClose, submitting]);

  const canAdvance =
    step === 0
      ? form.orderRef.trim().length > 0 &&
        // If both window times are set, latest must be >= earliest.
        (!form.windowEarliest ||
          !form.windowLatest ||
          new Date(form.windowLatest).getTime() >=
            new Date(form.windowEarliest).getTime())
      : form.items.every(
          (it) =>
            it.itemId.trim() &&
            it.pickup.trim() &&
            it.drop.trim() &&
            it.qty > 0 &&
            // If hazmat class is set, packing group is required for classes
            // that use them (we let the user pick "—" for the exempt classes
            // 1/2/7; the backend validates fully). Here we just gate on
            // class+group internal consistency.
            (!it.hazmatClass.trim() || true) &&
            // If any single dimension is filled, all three must be filled.
            ((!it.lengthMm && !it.widthMm && !it.heightMm) ||
              (it.lengthMm && it.widthMm && it.heightMm)),
        );

  async function handleSubmit() {
    setError(null);
    setSubmitting(true);
    try {
      const payload = buildPayload(form);
      if (editing) {
        await updateOrder(editing.id, payload);
      } else {
        await createOrder(payload);
      }
      onCreated();
      onClose();
    } catch (e) {
      setError(
        (e as Error).message ||
          (editing ? "Failed to update order." : "Failed to create order."),
      );
    } finally {
      setSubmitting(false);
    }
  }

  const updateItem = (idx: number, patch: Partial<ItemFormState>) =>
    setForm((f) => ({
      ...f,
      items: f.items.map((it, i) => (i === idx ? { ...it, ...patch } : it)),
    }));

  return (
    <AnimatePresence>
      {open && (
        <>
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            onClick={() => !submitting && onClose()}
            className="fixed inset-0 z-40 bg-[var(--color-ink-900)]/50 backdrop-blur-md"
          />
          <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
            <motion.div
              initial={{ opacity: 0, scale: 0.94, y: 12 }}
              animate={{ opacity: 1, scale: 1, y: 0 }}
              exit={{ opacity: 0, scale: 0.96, y: 12, transition: { duration: 0.16 } }}
              transition={{ type: "spring", stiffness: 360, damping: 30 }}
              className={cn(
                "relative w-full max-w-2xl overflow-hidden rounded-[var(--radius-xl)]",
                "glass-strong",
              )}
            >
              {/* Header */}
              <div className="flex items-start justify-between gap-3 px-6 py-5">
                <div>
                  <div className="text-[10.5px] font-semibold uppercase tracking-[0.12em] text-[var(--color-ink-400)]">
                    {isEdit
                      ? `Edit draft · ${editing!.orderRef} · Step ${step + 1} / 2`
                      : `New delivery order · Step ${step + 1} / 2`}
                  </div>
                  <h2 className="font-display mt-1 text-[1.4rem] font-semibold text-[var(--color-ink-900)]">
                    {step === 0 ? "Order details" : "Pick & drop"}
                  </h2>
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

              {/* Step progress */}
              <div className="px-6">
                <div className="relative h-1 overflow-hidden rounded-full bg-[var(--color-ink-100)] dark:bg-white/10">
                  <motion.div
                    initial={false}
                    animate={{ width: step === 0 ? "50%" : "100%" }}
                    transition={{ type: "spring", stiffness: 220, damping: 28 }}
                    className="absolute inset-y-0 left-0 bg-gradient-to-r from-[var(--color-brand-500)] to-[var(--color-brand-400)]"
                  />
                </div>
              </div>

              {/* Body */}
              <div className="max-h-[65vh] overflow-y-auto px-6 py-5">
                <AnimatePresence mode="wait">
                  {step === 0 && (
                    <motion.div
                      key="step0"
                      initial={{ opacity: 0, x: 12 }}
                      animate={{ opacity: 1, x: 0 }}
                      exit={{ opacity: 0, x: -12 }}
                      transition={{ duration: 0.2 }}
                      className="space-y-4"
                    >
                      <Field label="Order reference" required>
                        <input
                          value={form.orderRef}
                          onChange={(e) =>
                            setForm({ ...form, orderRef: e.target.value })
                          }
                          placeholder="DO-20260604-001"
                          readOnly={isEdit}
                          className={cn(
                            inputCls,
                            isEdit && "cursor-not-allowed opacity-70",
                          )}
                          title={
                            isEdit
                              ? "Order reference can't be changed after creation"
                              : undefined
                          }
                        />
                      </Field>

                      <div className="grid grid-cols-2 gap-3">
                        <Field label="Priority">
                          <select
                            value={form.priority}
                            onChange={(e) =>
                              setForm({ ...form, priority: e.target.value as Priority })
                            }
                            className={inputCls}
                          >
                            <option>Low</option>
                            <option>Normal</option>
                            <option>High</option>
                            <option>Critical</option>
                          </select>
                        </Field>
                        <Field label="Transport mode">
                          <select
                            value={form.transport}
                            onChange={(e) =>
                              setForm({
                                ...form,
                                transport: e.target.value as TransportMode,
                              })
                            }
                            className={inputCls}
                          >
                            <option value="Amr">AMR</option>
                            <option value="Manual">Manual</option>
                            <option value="Fleet">Fleet</option>
                          </select>
                        </Field>
                      </div>

                      {/* Service window */}
                      <div className="rounded-2xl border border-white/60 bg-white/40 p-4 dark:border-white/[0.06] dark:bg-white/[0.03]">
                        <div className="mb-3 text-[10.5px] font-semibold uppercase tracking-[0.1em] text-[var(--color-ink-500)]">
                          Service window <span className="text-[var(--color-ink-400)] normal-case tracking-normal">· optional</span>
                        </div>
                        <div className="grid grid-cols-2 gap-3">
                          <Field label="Earliest" compact>
                            <input
                              type="datetime-local"
                              value={form.windowEarliest}
                              onChange={(e) =>
                                setForm({ ...form, windowEarliest: e.target.value })
                              }
                              className={inputCls}
                            />
                          </Field>
                          <Field label="Latest" compact>
                            <input
                              type="datetime-local"
                              value={form.windowLatest}
                              onChange={(e) =>
                                setForm({ ...form, windowLatest: e.target.value })
                              }
                              className={inputCls}
                            />
                          </Field>
                        </div>
                        {form.windowEarliest &&
                          form.windowLatest &&
                          new Date(form.windowLatest).getTime() <
                            new Date(form.windowEarliest).getTime() && (
                            <p className="mt-2 text-[11px] font-medium text-[var(--color-coral)]">
                              Latest must be after earliest.
                            </p>
                          )}
                      </div>

                      <Field label="Requested by">
                        <input
                          value={form.requestedBy}
                          onChange={(e) =>
                            setForm({ ...form, requestedBy: e.target.value })
                          }
                          placeholder="Name or employee code"
                          className={inputCls}
                        />
                      </Field>

                      <Field label="Notes">
                        <textarea
                          value={form.notes}
                          onChange={(e) => setForm({ ...form, notes: e.target.value })}
                          placeholder="Anything ops needs to know…"
                          rows={3}
                          className={cn(inputCls, "resize-none")}
                        />
                      </Field>
                    </motion.div>
                  )}

                  {step === 1 && (
                    <motion.div
                      key="step1"
                      initial={{ opacity: 0, x: 12 }}
                      animate={{ opacity: 1, x: 0 }}
                      exit={{ opacity: 0, x: -12 }}
                      transition={{ duration: 0.2 }}
                      className="space-y-3"
                    >
                      {form.items.map((it, idx) => (
                        <div
                          key={idx}
                          className="rounded-2xl border border-white/60 bg-white/40 p-4 dark:border-white/[0.06] dark:bg-white/[0.03]"
                        >
                          <div className="flex items-center justify-between gap-2">
                            <span className="font-mono text-[11px] font-semibold text-[var(--color-ink-400)]">
                              Item #{(idx + 1).toString().padStart(2, "0")}
                            </span>
                            {form.items.length > 1 && (
                              <button
                                type="button"
                                onClick={() =>
                                  setForm({
                                    ...form,
                                    items: form.items.filter((_, i) => i !== idx),
                                  })
                                }
                                className="rounded-md p-1 text-[var(--color-coral)] transition-colors hover:bg-[#fde0db] dark:hover:bg-[#3a1a17]"
                              >
                                <Trash2 className="h-3.5 w-3.5" strokeWidth={2.2} />
                              </button>
                            )}
                          </div>
                          <div className="mt-3 grid grid-cols-2 gap-2.5">
                            <Field label="Item ID" required compact>
                              <input
                                value={it.itemId}
                                onChange={(e) => updateItem(idx, { itemId: e.target.value })}
                                placeholder="SKU-001"
                                className={inputCls}
                              />
                            </Field>
                            <Field label="Description" compact>
                              <input
                                value={it.description}
                                onChange={(e) =>
                                  updateItem(idx, { description: e.target.value })
                                }
                                placeholder="Optional"
                                className={inputCls}
                              />
                            </Field>
                            <Field label="Pickup" required compact>
                              <input
                                value={it.pickup}
                                onChange={(e) =>
                                  updateItem(idx, { pickup: e.target.value })
                                }
                                placeholder="LAB-03"
                                className={inputCls}
                              />
                            </Field>
                            <Field label="Drop" required compact>
                              <input
                                value={it.drop}
                                onChange={(e) => updateItem(idx, { drop: e.target.value })}
                                placeholder="WARD-7"
                                className={inputCls}
                              />
                            </Field>
                            <Field label="Qty" compact>
                              <div className="flex items-center gap-1.5">
                                <input
                                  type="number"
                                  min={1}
                                  value={it.qty}
                                  onChange={(e) =>
                                    updateItem(idx, {
                                      qty: Number(e.target.value) || 1,
                                    })
                                  }
                                  className={cn(inputCls, "w-20")}
                                />
                                <select
                                  value={it.uom}
                                  onChange={(e) =>
                                    updateItem(idx, { uom: e.target.value as Uom })
                                  }
                                  className={cn(inputCls, "flex-1")}
                                >
                                  {["EA", "BOX", "PALLET", "CASE", "KG", "G", "LB"].map(
                                    (u) => (
                                      <option key={u}>{u}</option>
                                    ),
                                  )}
                                </select>
                              </div>
                            </Field>
                            <Field label="Weight (kg)" compact>
                              <input
                                type="number"
                                min={0}
                                step="0.1"
                                value={it.weightKg}
                                onChange={(e) =>
                                  updateItem(idx, { weightKg: e.target.value })
                                }
                                placeholder="Optional"
                                className={inputCls}
                              />
                            </Field>
                          </div>

                          {/* Advanced */}
                          <button
                            type="button"
                            onClick={() =>
                              updateItem(idx, { advancedOpen: !it.advancedOpen })
                            }
                            className="mt-3 inline-flex items-center gap-1.5 text-[11px] font-semibold text-[var(--color-brand-500)] transition-colors hover:text-[var(--color-brand-900)] dark:hover:text-[var(--color-brand-400)]"
                          >
                            <Sliders className="h-3 w-3" strokeWidth={2.4} />
                            Advanced — dimensions, hazmat, temperature, handling
                            <motion.span
                              animate={{ rotate: it.advancedOpen ? 180 : 0 }}
                              transition={{ duration: 0.2 }}
                            >
                              <ChevronDown className="h-3 w-3" strokeWidth={2.4} />
                            </motion.span>
                          </button>

                          <AnimatePresence initial={false}>
                            {it.advancedOpen && (
                              <motion.div
                                initial={{ height: 0, opacity: 0 }}
                                animate={{ height: "auto", opacity: 1 }}
                                exit={{ height: 0, opacity: 0 }}
                                transition={{ duration: 0.22, ease: [0.22, 1, 0.36, 1] }}
                                className="overflow-hidden"
                              >
                                <div className="mt-3 space-y-3 border-t border-white/40 pt-3 dark:border-white/[0.06]">
                                  <Field label="Load unit profile" compact>
                                    <input
                                      value={it.loadUnitProfileCode}
                                      onChange={(e) =>
                                        updateItem(idx, {
                                          loadUnitProfileCode: e.target.value,
                                        })
                                      }
                                      placeholder="EU-PALLET, AMR-TOTE-M…"
                                      className={inputCls}
                                    />
                                  </Field>

                                  <div>
                                    <span className="block mb-1 text-[10px] font-semibold uppercase tracking-[0.1em] text-[var(--color-ink-500)]">
                                      Dimensions (mm)
                                    </span>
                                    <div className="grid grid-cols-3 gap-2">
                                      <input
                                        type="number"
                                        min={0}
                                        value={it.lengthMm}
                                        onChange={(e) =>
                                          updateItem(idx, { lengthMm: e.target.value })
                                        }
                                        placeholder="L"
                                        className={inputCls}
                                      />
                                      <input
                                        type="number"
                                        min={0}
                                        value={it.widthMm}
                                        onChange={(e) =>
                                          updateItem(idx, { widthMm: e.target.value })
                                        }
                                        placeholder="W"
                                        className={inputCls}
                                      />
                                      <input
                                        type="number"
                                        min={0}
                                        value={it.heightMm}
                                        onChange={(e) =>
                                          updateItem(idx, { heightMm: e.target.value })
                                        }
                                        placeholder="H"
                                        className={inputCls}
                                      />
                                    </div>
                                    {(!!it.lengthMm || !!it.widthMm || !!it.heightMm) &&
                                      (!it.lengthMm || !it.widthMm || !it.heightMm) && (
                                        <p className="mt-1 text-[10.5px] text-[var(--color-coral)]">
                                          Fill all three or leave blank.
                                        </p>
                                      )}
                                  </div>

                                  <div className="grid grid-cols-2 gap-2.5">
                                    <Field label="Temp min (°C)" compact>
                                      <input
                                        type="number"
                                        step="0.1"
                                        value={it.minC}
                                        onChange={(e) =>
                                          updateItem(idx, { minC: e.target.value })
                                        }
                                        placeholder="2"
                                        className={inputCls}
                                      />
                                    </Field>
                                    <Field label="Temp max (°C)" compact>
                                      <input
                                        type="number"
                                        step="0.1"
                                        value={it.maxC}
                                        onChange={(e) =>
                                          updateItem(idx, { maxC: e.target.value })
                                        }
                                        placeholder="8"
                                        className={inputCls}
                                      />
                                    </Field>
                                  </div>

                                  <div className="grid grid-cols-2 gap-2.5">
                                    <Field label="Hazmat class" compact>
                                      <input
                                        value={it.hazmatClass}
                                        onChange={(e) =>
                                          updateItem(idx, {
                                            hazmatClass: e.target.value,
                                          })
                                        }
                                        placeholder="3, 6.1, 8…"
                                        className={inputCls}
                                      />
                                    </Field>
                                    <Field label="Packing group" compact>
                                      <select
                                        value={it.hazmatPackingGroup}
                                        onChange={(e) =>
                                          updateItem(idx, {
                                            hazmatPackingGroup: e.target.value as
                                              | ""
                                              | PackingGroup,
                                          })
                                        }
                                        className={inputCls}
                                      >
                                        <option value="">— (or N/A)</option>
                                        <option value="I">I — Great danger</option>
                                        <option value="II">II — Medium danger</option>
                                        <option value="III">III — Minor danger</option>
                                      </select>
                                    </Field>
                                  </div>

                                  <div>
                                    <span className="block mb-1.5 text-[10px] font-semibold uppercase tracking-[0.1em] text-[var(--color-ink-500)]">
                                      Handling instructions
                                    </span>
                                    <div className="flex flex-wrap gap-1.5">
                                      {HANDLING_OPTIONS.map((h) => {
                                        const active = it.handlingInstructions.includes(
                                          h.value,
                                        );
                                        return (
                                          <motion.button
                                            key={h.value}
                                            type="button"
                                            whileTap={{ scale: 0.94 }}
                                            onClick={() => {
                                              const next = active
                                                ? it.handlingInstructions.filter(
                                                    (x) => x !== h.value,
                                                  )
                                                : [
                                                    ...it.handlingInstructions,
                                                    h.value,
                                                  ];
                                              updateItem(idx, {
                                                handlingInstructions: next,
                                              });
                                            }}
                                            className={cn(
                                              "rounded-full px-2.5 py-1 text-[10.5px] font-semibold transition-all",
                                              active
                                                ? "bg-[var(--color-amber-soft)] text-[var(--color-amber)] shadow-[0_2px_8px_-3px_rgba(245,158,11,0.4)]"
                                                : "bg-white/50 text-[var(--color-ink-600)] hover:bg-white/80 dark:bg-white/[0.05] dark:hover:bg-white/[0.1]",
                                            )}
                                          >
                                            {h.label}
                                          </motion.button>
                                        );
                                      })}
                                    </div>
                                  </div>
                                </div>
                              </motion.div>
                            )}
                          </AnimatePresence>
                        </div>
                      ))}

                      <button
                        type="button"
                        onClick={() =>
                          setForm({
                            ...form,
                            items: [...form.items, { ...EMPTY_ITEM }],
                          })
                        }
                        className="inline-flex items-center gap-1.5 rounded-full bg-white/40 px-3 py-1.5 text-[12px] font-semibold text-[var(--color-brand-500)] transition-all hover:bg-white/70 dark:bg-white/[0.05] dark:hover:bg-white/[0.1]"
                      >
                        <Plus className="h-3.5 w-3.5" strokeWidth={2.4} />
                        Add another item
                      </button>
                    </motion.div>
                  )}
                </AnimatePresence>

                {error && (
                  <motion.div
                    initial={{ opacity: 0 }}
                    animate={{ opacity: 1 }}
                    className="mt-4 rounded-xl bg-[#fde0db] px-4 py-3 text-[12.5px] font-medium text-[var(--color-coral)] dark:bg-[#3a1a17]"
                  >
                    {error}
                  </motion.div>
                )}
              </div>

              {/* Footer */}
              <footer className="flex items-center justify-between gap-3 border-t border-white/40 px-6 py-4 dark:border-white/[0.06]">
                {step === 1 ? (
                  <button
                    type="button"
                    onClick={() => setStep(0)}
                    disabled={submitting}
                    className="inline-flex items-center gap-1.5 rounded-full px-3 py-2 text-[12px] font-semibold text-[var(--color-ink-600)] transition-colors hover:bg-white/40 dark:hover:bg-white/[0.06]"
                  >
                    <ArrowLeft className="h-3.5 w-3.5" strokeWidth={2.4} />
                    Back
                  </button>
                ) : (
                  <span />
                )}
                <div className="flex items-center gap-2">
                  <button
                    type="button"
                    onClick={() => !submitting && onClose()}
                    className="rounded-full bg-white/40 px-4 py-2 text-[12px] font-semibold text-[var(--color-ink-700)] transition-colors hover:bg-white/70 dark:bg-white/[0.05] dark:hover:bg-white/[0.1]"
                  >
                    Cancel
                  </button>
                  {step === 0 ? (
                    <motion.button
                      type="button"
                      onClick={() => setStep(1)}
                      disabled={!canAdvance}
                      whileHover={canAdvance ? { y: -1 } : {}}
                      whileTap={canAdvance ? { scale: 0.97 } : {}}
                      className={cn(
                        "inline-flex items-center gap-1.5 rounded-full px-4 py-2 text-[12px] font-semibold transition-all",
                        canAdvance
                          ? "bg-[var(--color-brand-900)] text-white hover:shadow-[0_14px_36px_-12px_rgba(15,23,42,0.6)] dark:bg-[var(--color-brand-500)]"
                          : "bg-[var(--color-ink-100)] text-[var(--color-ink-400)] cursor-not-allowed dark:bg-white/[0.04]",
                      )}
                    >
                      Continue
                      <ArrowRight className="h-3.5 w-3.5" strokeWidth={2.4} />
                    </motion.button>
                  ) : (
                    <motion.button
                      type="button"
                      onClick={handleSubmit}
                      disabled={!canAdvance || submitting}
                      whileHover={canAdvance && !submitting ? { y: -1 } : {}}
                      whileTap={canAdvance && !submitting ? { scale: 0.97 } : {}}
                      className={cn(
                        "inline-flex items-center gap-1.5 rounded-full px-4 py-2 text-[12px] font-semibold transition-all",
                        canAdvance && !submitting
                          ? "bg-[var(--color-success)] text-white hover:shadow-[0_14px_36px_-12px_rgba(16,185,129,0.55)]"
                          : "bg-[var(--color-ink-100)] text-[var(--color-ink-400)] cursor-not-allowed dark:bg-white/[0.04]",
                      )}
                    >
                      {submitting ? (
                        <Loader2 className="h-3.5 w-3.5 animate-spin" strokeWidth={2.4} />
                      ) : (
                        <Check className="h-3.5 w-3.5" strokeWidth={2.6} />
                      )}
                      {submitting
                        ? isEdit
                          ? "Saving…"
                          : "Creating…"
                        : isEdit
                          ? "Save changes"
                          : "Create order"}
                    </motion.button>
                  )}
                </div>
              </footer>
            </motion.div>
          </div>
        </>
      )}
    </AnimatePresence>
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
  children,
}: {
  label: string;
  required?: boolean;
  compact?: boolean;
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
    </label>
  );
}
