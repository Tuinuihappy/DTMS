"use client";

import { useEffect, useMemo, useState } from "react";
import { AnimatePresence, motion } from "motion/react";
import {
  AlertTriangle,
  BatteryCharging,
  CheckCircle2,
  Clock,
  Crosshair,
  Hexagon,
  Layers,
  Loader2,
  MapPin,
  PackageOpen,
  ParkingCircle,
  Power,
  PowerOff,
  Save,
  X,
} from "lucide-react";

import { cn } from "@/lib/utils";
import type { StationDto, StationTypeWire } from "@/lib/api/facility";

type StationOp =
  | { kind: "update"; type: StationTypeWire; code: string | null }
  | { kind: "force-offline"; reason: string; durationMinutes: number }
  | { kind: "clear-override" };

type TypeMeta = {
  key: StationTypeWire;
  label: string;
  swatch: string;
  icon: typeof MapPin;
};

const TYPES: TypeMeta[] = [
  { key: "NORMAL", label: "Waypoint", swatch: "var(--color-brand-500)", icon: MapPin },
  { key: "CHARGING", label: "Charging", swatch: "var(--color-amber)", icon: BatteryCharging },
  { key: "PICKUP", label: "Pickup", swatch: "#e07248", icon: PackageOpen },
  { key: "DROPOFF", label: "Drop-off", swatch: "#16a37b", icon: Layers },
  { key: "PARKING", label: "Parking", swatch: "#7c6acd", icon: ParkingCircle },
  { key: "DOCK", label: "Dock", swatch: "#3b6bd6", icon: Hexagon },
  { key: "CHECKPOINT", label: "Checkpoint", swatch: "var(--color-ink-500)", icon: Crosshair },
];

function normalizeType(raw: string | undefined | null): StationTypeWire {
  const t = (raw ?? "NORMAL").toString().toUpperCase();
  return (TYPES.map((x) => x.key) as string[]).includes(t)
    ? (t as StationTypeWire)
    : "NORMAL";
}

function formatExpiry(iso: string | null | undefined): string {
  if (!iso) return "—";
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return "—";
  const diff = d.getTime() - Date.now();
  const mins = Math.round(diff / 60_000);
  if (mins <= 0) return "expired";
  if (mins < 60) return `in ${mins} min`;
  const hrs = Math.round(mins / 60);
  return `in ${hrs} hr`;
}

export function StationEditDrawer({
  open,
  station,
  busy = false,
  onClose,
  onSubmit,
}: {
  open: boolean;
  station: StationDto | null;
  busy?: boolean;
  onClose: () => void;
  // Two-arg callback so the experience can route the right operation through
  // the right API. Drawer doesn't know about endpoints — it only emits intent.
  onSubmit: (station: StationDto, op: StationOp) => Promise<void>;
}) {
  const initialType = normalizeType(station?.type);
  const initialCode = station?.code ?? "";
  const [type, setType] = useState<StationTypeWire>(initialType);
  const [code, setCode] = useState(initialCode);
  const [reason, setReason] = useState("");
  const [duration, setDuration] = useState(30);
  const [errors, setErrors] = useState<{ duration?: string; reason?: string }>({});

  // Reset form whenever a different station is shown.
  useEffect(() => {
    if (!station) return;
    setType(normalizeType(station.type));
    setCode(station.code ?? "");
    setReason("");
    setDuration(30);
    setErrors({});
  }, [station?.id]); // eslint-disable-line react-hooks/exhaustive-deps

  const dirty = useMemo(() => {
    if (!station) return false;
    return (
      type !== normalizeType(station.type) ||
      (code.trim().toUpperCase() || null) !== (station.code ?? null)
    );
  }, [station, type, code]);

  if (!station) return null;
  const currentMeta = TYPES.find((t) => t.key === type) ?? TYPES[0];
  const isOffline = station.isManualOverrideActive;

  async function handleSaveProfile() {
    if (!station || !dirty) return;
    await onSubmit(station, {
      kind: "update",
      type,
      code: code.trim() ? code.trim().toUpperCase() : null,
    });
  }

  async function handleForceOffline() {
    if (!station) return;
    const errs: typeof errors = {};
    if (!reason.trim()) errs.reason = "Reason is required.";
    if (duration < 5 || duration > 1440)
      errs.duration = "Duration must be between 5 and 1440 minutes.";
    if (Object.keys(errs).length > 0) {
      setErrors(errs);
      return;
    }
    await onSubmit(station, {
      kind: "force-offline",
      reason: reason.trim(),
      durationMinutes: duration,
    });
  }

  async function handleClear() {
    if (!station) return;
    await onSubmit(station, { kind: "clear-override" });
  }

  return (
    <AnimatePresence>
      {open && (
        <>
          <motion.div
            key="backdrop"
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            onClick={() => !busy && onClose()}
            className="fixed inset-0 z-40 bg-[var(--color-ink-900)]/40 backdrop-blur-sm"
          />
          <motion.aside
            key="drawer"
            initial={{ x: "100%" }}
            animate={{ x: 0 }}
            exit={{ x: "100%" }}
            transition={{ type: "spring", stiffness: 320, damping: 34 }}
            className="fixed inset-y-0 right-0 z-50 flex w-full max-w-md flex-col bg-[var(--color-canvas)]/95 backdrop-blur-2xl shadow-[-20px_0_60px_-20px_rgba(15,23,42,0.35)] dark:bg-[var(--color-canvas)]/90"
          >
            {/* Header */}
            <div className="relative overflow-hidden">
              <div
                aria-hidden
                className="absolute inset-0 pointer-events-none"
                style={{
                  background: `radial-gradient(60% 80% at 0% 0%, ${currentMeta.swatch}22, transparent 65%), radial-gradient(60% 80% at 100% 100%, var(--color-pastel-sky)55, transparent 65%)`,
                }}
              />
              <div className="relative flex items-start justify-between gap-3 px-6 pt-6 pb-5">
                <div className="flex items-start gap-3 min-w-0">
                  <span
                    className="grid h-11 w-11 place-items-center rounded-[14px] text-white shadow-[inset_0_1px_0_rgba(255,255,255,0.4),0_8px_18px_-8px_rgba(15,23,42,0.35)] shrink-0"
                    style={{ background: currentMeta.swatch }}
                  >
                    <currentMeta.icon className="h-5 w-5" strokeWidth={2.1} />
                  </span>
                  <div className="min-w-0">
                    <div className="flex items-center gap-2 flex-wrap">
                      <span className="text-[10.5px] font-semibold uppercase tracking-[0.14em] text-[var(--color-ink-400)]">
                        Station
                      </span>
                      <StatusBadge active={station.isActive} forced={isOffline} />
                    </div>
                    <h2 className="font-display mt-1 truncate text-[1.4rem] font-semibold text-[var(--color-ink-900)]">
                      {station.name}
                    </h2>
                    <div className="mt-1 flex items-center gap-2 font-mono text-[10.5px] tracking-tight text-[var(--color-ink-500)]">
                      <span>
                        {Math.round(station.x).toLocaleString("en-US")},{" "}
                        {Math.round(station.y).toLocaleString("en-US")}
                      </span>
                      {station.vendorRef && (
                        <>
                          <span className="opacity-50">·</span>
                          <span>RIOT {station.vendorRef}</span>
                        </>
                      )}
                    </div>
                  </div>
                </div>
                <button
                  type="button"
                  onClick={() => !busy && onClose()}
                  className="rounded-full p-2 text-[var(--color-ink-500)] transition-colors hover:bg-white/50 hover:text-[var(--color-ink-900)] dark:hover:bg-white/10 shrink-0 cursor-pointer"
                  aria-label="Close"
                >
                  <X className="h-4 w-4" strokeWidth={2.4} />
                </button>
              </div>
            </div>

            {/* Body */}
            <div className="flex-1 overflow-y-auto px-6 py-5 space-y-7">
              {/* Profile section */}
              <section>
                <SectionLabel
                  title="Profile"
                  hint="Code + type are operator-editable. Name + coords come from RIOT3."
                />

                <Field label="Code" hint="Used in delivery-order submissions">
                  <input
                    value={code}
                    onChange={(e) => setCode(e.target.value)}
                    placeholder="e.g. WH-NORTH-3"
                    maxLength={50}
                    disabled={busy}
                    className="h-10 w-full rounded-[12px] bg-white/70 dark:bg-white/[0.05] border border-[var(--color-ink-100)]/70 dark:border-white/[0.06] px-3 text-[13px] font-mono tabular-nums tracking-tight text-[var(--color-ink-900)] placeholder:text-[var(--color-ink-300)] focus:outline-none focus:ring-2 focus:ring-[var(--color-brand-500)]/40"
                  />
                </Field>

                <Field label="Type">
                  <div className="grid grid-cols-2 gap-1.5">
                    {TYPES.map((t) => {
                      const active = t.key === type;
                      return (
                        <button
                          key={t.key}
                          type="button"
                          onClick={() => setType(t.key)}
                          disabled={busy}
                          className={cn(
                            "group flex items-center gap-2 rounded-[12px] px-3 py-2 text-left transition-all cursor-pointer border",
                            active
                              ? "bg-white dark:bg-white/[0.08] border-transparent ring-2 ring-[var(--color-brand-200)] dark:ring-[var(--color-brand-500)]/40 shadow-[0_8px_18px_-10px_rgba(15,23,42,0.2)]"
                              : "bg-white/55 dark:bg-white/[0.04] border-white/70 dark:border-white/[0.06] hover:bg-white/80",
                          )}
                        >
                          <span
                            className="grid h-7 w-7 place-items-center rounded-[9px] text-white shrink-0"
                            style={{ background: t.swatch }}
                          >
                            <t.icon className="h-3.5 w-3.5" strokeWidth={2.2} />
                          </span>
                          <span className="text-[12px] font-semibold tracking-tight text-[var(--color-ink-800)] truncate">
                            {t.label}
                          </span>
                        </button>
                      );
                    })}
                  </div>
                </Field>

                <button
                  type="button"
                  onClick={handleSaveProfile}
                  disabled={!dirty || busy}
                  className={cn(
                    "mt-3 inline-flex items-center justify-center gap-2 w-full h-10 rounded-full text-[12.5px] font-semibold tracking-tight transition-all cursor-pointer",
                    "bg-[var(--color-brand-900)] text-white",
                    "shadow-[inset_0_1px_0_rgba(255,255,255,0.18),0_10px_24px_-10px_rgba(14,21,48,0.55)]",
                    "hover:-translate-y-px disabled:opacity-50 disabled:cursor-not-allowed disabled:translate-y-0",
                    "dark:bg-[var(--color-brand-500)]",
                  )}
                >
                  {busy ? (
                    <Loader2 className="h-3.5 w-3.5 animate-spin" strokeWidth={2.5} />
                  ) : (
                    <Save className="h-3.5 w-3.5" strokeWidth={2.5} />
                  )}
                  Save changes
                </button>
              </section>

              {/* Force-offline section */}
              <section className="relative">
                <SectionLabel
                  title="Operator override"
                  hint="Take this station out of rotation without touching RIOT3 sync."
                />

                {isOffline ? (
                  <div className="rounded-[14px] bg-[var(--color-amber-soft)] dark:bg-[var(--color-amber-soft)]/30 border border-[var(--color-amber)]/40 p-4">
                    <div className="flex items-start gap-2.5">
                      <span className="grid h-7 w-7 place-items-center rounded-[10px] bg-[var(--color-amber)] text-white shrink-0">
                        <PowerOff className="h-3.5 w-3.5" strokeWidth={2.3} />
                      </span>
                      <div className="min-w-0">
                        <div className="text-[12.5px] font-semibold tracking-tight text-[#7a3d05] dark:text-[var(--color-amber)]">
                          Currently force-offline
                        </div>
                        <p className="mt-1 text-[11.5px] text-[#8a4a07] dark:text-[var(--color-amber)]/80">
                          {station.manualOverrideOffline ? "Until cleared" : "Pending sync"} ·{" "}
                          expires {formatExpiry(station.manualOverrideExpiresAt)}
                        </p>
                        {station.manualOverrideReason && (
                          <p className="mt-2 rounded-[8px] bg-white/55 dark:bg-white/[0.06] px-2.5 py-1.5 text-[11.5px] text-[var(--color-ink-700)]">
                            “{station.manualOverrideReason}”
                          </p>
                        )}
                        {station.manualOverrideBy && (
                          <p className="mt-1.5 font-mono text-[10.5px] text-[var(--color-ink-500)]">
                            by {station.manualOverrideBy}
                          </p>
                        )}
                      </div>
                    </div>
                    <button
                      type="button"
                      onClick={handleClear}
                      disabled={busy}
                      className="mt-4 inline-flex items-center justify-center gap-2 w-full h-9 rounded-full bg-white dark:bg-white/[0.06] text-[var(--color-ink-800)] text-[12px] font-semibold tracking-tight cursor-pointer hover:bg-white/85 disabled:opacity-50 disabled:cursor-not-allowed"
                    >
                      {busy ? (
                        <Loader2 className="h-3.5 w-3.5 animate-spin" strokeWidth={2.5} />
                      ) : (
                        <Power className="h-3.5 w-3.5" strokeWidth={2.5} />
                      )}
                      Restore station
                    </button>
                  </div>
                ) : (
                  <>
                    <Field
                      label="Reason"
                      hint="Visible in audit log + ops chat"
                      error={errors.reason}
                    >
                      <textarea
                        value={reason}
                        onChange={(e) => setReason(e.target.value)}
                        placeholder="e.g. Forklift blocking pickup lane"
                        rows={2}
                        maxLength={200}
                        disabled={busy}
                        className="w-full rounded-[12px] bg-white/70 dark:bg-white/[0.05] border border-[var(--color-ink-100)]/70 dark:border-white/[0.06] px-3 py-2 text-[12.5px] tracking-tight text-[var(--color-ink-900)] placeholder:text-[var(--color-ink-300)] focus:outline-none focus:ring-2 focus:ring-[var(--color-amber)]/40 resize-none"
                      />
                    </Field>

                    <Field
                      label="Duration"
                      hint="5 to 1,440 minutes (24 hr)"
                      error={errors.duration}
                    >
                      <div className="flex items-center gap-2">
                        <input
                          type="number"
                          min={5}
                          max={1440}
                          value={duration}
                          onChange={(e) => setDuration(Number(e.target.value))}
                          disabled={busy}
                          className="h-10 w-28 rounded-[12px] bg-white/70 dark:bg-white/[0.05] border border-[var(--color-ink-100)]/70 dark:border-white/[0.06] px-3 text-[13px] font-mono tabular-nums tracking-tight text-[var(--color-ink-900)] focus:outline-none focus:ring-2 focus:ring-[var(--color-amber)]/40"
                        />
                        <span className="text-[11.5px] font-medium text-[var(--color-ink-500)]">
                          minutes
                        </span>
                        <div className="ml-auto flex items-center gap-1">
                          {[15, 60, 240].map((m) => (
                            <button
                              key={m}
                              type="button"
                              onClick={() => setDuration(m)}
                              disabled={busy}
                              className={cn(
                                "h-7 rounded-full px-2 text-[10.5px] font-semibold cursor-pointer transition-colors",
                                duration === m
                                  ? "bg-[var(--color-amber)] text-white"
                                  : "bg-white/55 dark:bg-white/[0.05] text-[var(--color-ink-700)] hover:bg-white/85",
                              )}
                            >
                              {m < 60 ? `${m}m` : `${m / 60}h`}
                            </button>
                          ))}
                        </div>
                      </div>
                    </Field>

                    <button
                      type="button"
                      onClick={handleForceOffline}
                      disabled={busy}
                      className={cn(
                        "mt-3 inline-flex items-center justify-center gap-2 w-full h-10 rounded-full text-[12.5px] font-semibold tracking-tight transition-all cursor-pointer",
                        "bg-[var(--color-amber)] text-white",
                        "shadow-[inset_0_1px_0_rgba(255,255,255,0.25),0_10px_24px_-10px_rgba(245,158,11,0.6)]",
                        "hover:-translate-y-px disabled:opacity-50 disabled:cursor-not-allowed disabled:translate-y-0",
                      )}
                    >
                      {busy ? (
                        <Loader2 className="h-3.5 w-3.5 animate-spin" strokeWidth={2.5} />
                      ) : (
                        <PowerOff className="h-3.5 w-3.5" strokeWidth={2.5} />
                      )}
                      Force station offline
                    </button>
                  </>
                )}
              </section>

              {/* Read-only audit */}
              <section>
                <SectionLabel title="Source" />
                <dl className="grid grid-cols-2 gap-2 text-[11.5px]">
                  <Row icon={<MapPin className="h-3 w-3" />} label="x" value={Math.round(station.x).toLocaleString("en-US")} mono />
                  <Row icon={<MapPin className="h-3 w-3" />} label="y" value={Math.round(station.y).toLocaleString("en-US")} mono />
                  <Row
                    icon={<Crosshair className="h-3 w-3" />}
                    label="θ"
                    value={station.theta !== null ? `${station.theta.toFixed(3)} rad` : "—"}
                    mono
                  />
                  <Row
                    icon={<Clock className="h-3 w-3" />}
                    label="RIOT id"
                    value={station.vendorRef ?? "manual"}
                    mono
                  />
                </dl>
              </section>
            </div>
          </motion.aside>
        </>
      )}
    </AnimatePresence>
  );
}

function SectionLabel({ title, hint }: { title: string; hint?: string }) {
  return (
    <div className="mb-3">
      <div className="text-[10px] font-bold uppercase tracking-[0.18em] text-[var(--color-ink-400)]">
        {title}
      </div>
      {hint && (
        <p className="mt-0.5 text-[11.5px] text-[var(--color-ink-500)] max-w-[34ch]">
          {hint}
        </p>
      )}
    </div>
  );
}

function Field({
  label,
  hint,
  error,
  children,
}: {
  label: string;
  hint?: string;
  error?: string;
  children: React.ReactNode;
}) {
  return (
    <div className="mt-3">
      <div className="flex items-baseline justify-between">
        <label className="text-[11.5px] font-semibold tracking-tight text-[var(--color-ink-700)]">
          {label}
        </label>
        {hint && (
          <span className="text-[10.5px] text-[var(--color-ink-400)]">{hint}</span>
        )}
      </div>
      <div className="mt-1.5">{children}</div>
      {error && (
        <p className="mt-1 text-[10.5px] text-[var(--color-coral)] font-medium">
          {error}
        </p>
      )}
    </div>
  );
}

function Row({
  icon,
  label,
  value,
  mono,
}: {
  icon: React.ReactNode;
  label: string;
  value: string;
  mono?: boolean;
}) {
  return (
    <div className="rounded-[10px] bg-white/55 dark:bg-white/[0.04] px-2.5 py-1.5">
      <div className="flex items-center gap-1 text-[9.5px] uppercase tracking-[0.14em] text-[var(--color-ink-400)]">
        {icon}
        {label}
      </div>
      <div
        className={cn(
          "mt-0.5 text-[12px] font-semibold text-[var(--color-ink-800)] truncate",
          mono && "font-mono tabular-nums tracking-tight",
        )}
      >
        {value}
      </div>
    </div>
  );
}

function StatusBadge({ active, forced }: { active: boolean; forced: boolean }) {
  if (forced) {
    return (
      <span className="inline-flex items-center gap-1 rounded-full bg-[var(--color-amber-soft)] px-1.5 py-0.5 text-[9.5px] font-bold tracking-[0.04em] text-[#8a4a07] dark:text-[var(--color-amber)]">
        <AlertTriangle className="h-2.5 w-2.5" strokeWidth={2.4} />
        offline
      </span>
    );
  }
  if (active) {
    return (
      <span className="inline-flex items-center gap-1 rounded-full bg-[var(--color-success-soft)] px-1.5 py-0.5 text-[9.5px] font-bold tracking-[0.04em] text-[var(--color-success)]">
        <CheckCircle2 className="h-2.5 w-2.5" strokeWidth={2.4} />
        active
      </span>
    );
  }
  return (
    <span className="inline-flex items-center gap-1 rounded-full bg-[var(--color-ink-100)] px-1.5 py-0.5 text-[9.5px] font-bold tracking-[0.04em] text-[var(--color-ink-700)]">
      inactive
    </span>
  );
}

export type { StationOp };
