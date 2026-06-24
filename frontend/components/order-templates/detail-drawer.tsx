"use client";

import { AnimatePresence, motion } from "motion/react";
import {
  Activity,
  Anchor,
  ArrowRight,
  Clock,
  Copy,
  FileStack,
  Hash,
  Layers,
  Map as MapIcon,
  Navigation,
  Pencil,
  Power,
  Rocket,
  Tag,
  Trash2,
  Truck,
  User,
  Workflow,
  X,
} from "lucide-react";
import Link from "next/link";
import { cn } from "@/lib/utils";
import type {
  OrderTemplateDto,
  OrderTemplateMissionDto,
} from "@/lib/api/order-templates";
import { DateTime } from "@/components/primitives/date-time";

export function OrderTemplateDrawer({
  open,
  template,
  busy = false,
  onClose,
  onDispatch,
  onToggleActive,
  onDelete,
}: {
  open: boolean;
  template: OrderTemplateDto | null;
  busy?: boolean;
  onClose: () => void;
  onDispatch: (t: OrderTemplateDto) => void;
  onToggleActive: (t: OrderTemplateDto) => void;
  onDelete: (t: OrderTemplateDto) => void;
}) {
  return (
    <AnimatePresence>
      {open && template && (
        <>
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            onClick={() => !busy && onClose()}
            className="fixed inset-0 z-40 bg-[var(--color-ink-900)]/40 backdrop-blur-sm"
          />
          <motion.aside
            initial={{ x: "100%" }}
            animate={{ x: 0 }}
            exit={{ x: "100%" }}
            transition={{ type: "spring", stiffness: 320, damping: 34 }}
            className="fixed inset-y-0 right-0 z-50 flex w-full max-w-lg flex-col bg-[var(--color-canvas)]/95 backdrop-blur-2xl shadow-[-20px_0_60px_-20px_rgba(15,23,42,0.35)] dark:bg-[var(--color-canvas)]/90"
          >
            {/* Header */}
            <div className="relative overflow-hidden">
              <div className="absolute inset-0 bg-gradient-to-br from-[var(--color-pastel-sky)]/55 via-transparent to-[var(--color-pastel-lavender)]/55 pointer-events-none" />
              <div className="relative flex items-start justify-between gap-3 px-6 pt-6 pb-5">
                <div className="flex items-start gap-3">
                  <span className="grid h-11 w-11 place-items-center rounded-[14px] bg-white/85 text-[var(--color-brand-900)] shadow-[inset_0_1px_0_rgba(255,255,255,0.9),0_6px_18px_-8px_rgba(15,23,42,0.25)] dark:bg-white/[0.08]">
                    <FileStack className="h-4 w-4" strokeWidth={2.1} />
                  </span>
                  <div className="min-w-0">
                    <div className="flex items-center gap-2">
                      <span className="text-[10.5px] font-semibold uppercase tracking-[0.14em] text-[var(--color-ink-400)]">
                        Order template
                      </span>
                      <ActiveDot active={template.isActive} />
                    </div>
                    <h2 className="font-display mt-1 truncate text-[1.45rem] font-semibold text-[var(--color-ink-900)]">
                      {template.name}
                    </h2>
                    <div className="mt-1.5 flex flex-wrap items-center gap-1.5">
                      <PriorityBadge value={template.priority} />
                      <StructureBadge
                        type={template.transportOrder?.structureType ?? "sequence"}
                      />
                      <span className="rounded-full bg-white/55 px-2 py-0.5 text-[10.5px] font-semibold text-[var(--color-ink-700)] dark:bg-white/[0.06]">
                        {template.transportOrder?.missions?.length ?? 0} mission
                        {(template.transportOrder?.missions?.length ?? 0) === 1
                          ? ""
                          : "s"}
                      </span>
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
            </div>

            {/* Body */}
            <div className="flex-1 space-y-6 overflow-y-auto px-6 py-5">
              {template.description && (
                <Section title="Description">
                  <p className="text-[13px] leading-relaxed text-[var(--color-ink-700)]">
                    {template.description}
                  </p>
                </Section>
              )}

              <Section
                title={`Mission journey · ${template.transportOrder?.missions?.length ?? 0}`}
              >
                <MissionTimeline missions={template.transportOrder?.missions ?? []} />
              </Section>

              {hasBinding(template) && (
                <Section title="Vehicle binding">
                  <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
                    {template.appointVehicleName || template.appointVehicleKey ? (
                      <BindingRow
                        icon={<Truck className="h-3.5 w-3.5" />}
                        label="Vehicle"
                        value={
                          template.appointVehicleName ?? template.appointVehicleKey ?? ""
                        }
                        sub={
                          template.appointVehicleName ? template.appointVehicleKey : null
                        }
                      />
                    ) : null}
                    {template.appointVehicleGroupName || template.appointVehicleGroupKey ? (
                      <BindingRow
                        icon={<Layers className="h-3.5 w-3.5" />}
                        label="Vehicle group"
                        value={
                          template.appointVehicleGroupName ??
                          template.appointVehicleGroupKey ??
                          ""
                        }
                        sub={
                          template.appointVehicleGroupName
                            ? template.appointVehicleGroupKey
                            : null
                        }
                      />
                    ) : null}
                    {template.appointQueueWaitArea ? (
                      <BindingRow
                        icon={<Anchor className="h-3.5 w-3.5" />}
                        label="Wait area"
                        value={template.appointQueueWaitArea}
                      />
                    ) : null}
                  </div>
                </Section>
              )}

              <Section title="Audit">
                <dl className="grid grid-cols-1 gap-3 text-[12.5px]">
                  <MetaRow
                    icon={<Hash className="h-3.5 w-3.5" />}
                    label="ID"
                    value={
                      <span className="font-mono text-[11.5px] text-[var(--color-ink-700)]">
                        {template.id}
                      </span>
                    }
                  />
                  <MetaRow
                    icon={<Clock className="h-3.5 w-3.5" />}
                    label="Created"
                    value={<DateTime value={template.createdAt} />}
                  />
                  <MetaRow
                    icon={<User className="h-3.5 w-3.5" />}
                    label="Created by"
                    value={template.createdBy ?? "—"}
                  />
                  <MetaRow
                    icon={<Clock className="h-3.5 w-3.5" />}
                    label="Modified"
                    value={<DateTime value={template.modifiedAt} />}
                  />
                  <MetaRow
                    icon={<User className="h-3.5 w-3.5" />}
                    label="Modified by"
                    value={template.modifiedBy ?? "—"}
                  />
                  <MetaRow
                    icon={<Activity className="h-3.5 w-3.5" />}
                    label="Status"
                    value={
                      <span
                        className={cn(
                          "rounded-full px-2 py-0.5 text-[10.5px] font-bold tracking-[0.06em]",
                          template.isActive
                            ? "bg-[var(--color-success-soft)] text-[var(--color-success)]"
                            : "bg-[var(--color-ink-100)] text-[var(--color-ink-500)]",
                        )}
                      >
                        {template.isActive ? "ACTIVE" : "INACTIVE"}
                      </span>
                    }
                  />
                </dl>
              </Section>
            </div>

            {/* Footer */}
            <div className="border-t border-white/60 bg-white/30 px-6 py-4 dark:border-white/[0.06] dark:bg-white/[0.02]">
              <div className="flex flex-wrap items-center justify-end gap-2">
                <DrawerAction
                  variant="ghost"
                  icon={<Trash2 className="h-3.5 w-3.5" />}
                  onClick={() => onDelete(template)}
                  disabled={busy}
                >
                  Delete
                </DrawerAction>
                <DrawerAction
                  variant={template.isActive ? "ghost" : "secondary"}
                  icon={<Power className="h-3.5 w-3.5" />}
                  onClick={() => onToggleActive(template)}
                  disabled={busy}
                >
                  {template.isActive ? "Deactivate" : "Activate"}
                </DrawerAction>
                <DrawerLink
                  variant="ghost"
                  icon={<Copy className="h-3.5 w-3.5" />}
                  href={`/delivery-orders/order-templates/${template.id}/duplicate`}
                >
                  Duplicate
                </DrawerLink>
                <DrawerLink
                  variant="ghost"
                  icon={<Pencil className="h-3.5 w-3.5" />}
                  href={`/delivery-orders/order-templates/${template.id}/edit`}
                >
                  Edit
                </DrawerLink>
                <DrawerAction
                  variant="primary"
                  icon={<Rocket className="h-3.5 w-3.5" />}
                  onClick={() => onDispatch(template)}
                  disabled={busy || !template.isActive}
                >
                  Create order
                </DrawerAction>
              </div>
            </div>
          </motion.aside>
        </>
      )}
    </AnimatePresence>
  );
}

// ── Mission timeline ───────────────────────────────────────────────────
// The drawer's showcase moment: a vertical journey that visualises the
// missions[] array as a chain of MOVE→ACT steps with type icons, station
// targets and action references.

function MissionTimeline({ missions }: { missions: OrderTemplateMissionDto[] }) {
  if (missions.length === 0) {
    return (
      <div className="rounded-[var(--radius-sm)] border border-dashed border-[var(--color-ink-200)] bg-white/40 px-4 py-6 text-center text-[12.5px] italic text-[var(--color-ink-500)] dark:border-white/[0.08] dark:bg-white/[0.02]">
        No missions defined for this template.
      </div>
    );
  }
  return (
    <ol className="relative space-y-2.5 pl-1">
      {missions.map((m, i) => (
        <MissionStep
          key={`${m.sequence}-${i}`}
          mission={m}
          index={i}
          isLast={i === missions.length - 1}
        />
      ))}
    </ol>
  );
}

function MissionStep({
  mission,
  index,
  isLast,
}: {
  mission: OrderTemplateMissionDto;
  index: number;
  isLast: boolean;
}) {
  const isMove = mission.type === "MOVE";
  return (
    <motion.li
      initial={{ opacity: 0, x: 8 }}
      animate={{ opacity: 1, x: 0 }}
      transition={{ delay: index * 0.05, duration: 0.32, ease: [0.22, 1, 0.36, 1] }}
      className="relative flex gap-3"
    >
      {/* Rail */}
      <div className="relative flex w-7 shrink-0 flex-col items-center">
        <span
          className={cn(
            "grid h-7 w-7 place-items-center rounded-full text-white shadow-[0_4px_12px_-4px_rgba(15,23,42,0.45)]",
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
        {!isLast && (
          <span
            aria-hidden
            className="mt-1 h-full w-px flex-1 bg-gradient-to-b from-[var(--color-ink-200)] to-transparent dark:from-white/15"
          />
        )}
      </div>

      {/* Card */}
      <div className="flex-1 pb-2.5">
        <div className="rounded-[var(--radius-sm)] border border-white/70 bg-white/65 px-3 py-2.5 backdrop-blur dark:border-white/[0.06] dark:bg-white/[0.04]">
          <div className="flex flex-wrap items-center gap-1.5">
            <span className="font-mono text-[10px] font-bold uppercase tracking-[0.12em] text-[var(--color-ink-400)]">
              #{mission.sequence + 1}
            </span>
            <span
              className={cn(
                "inline-flex items-center gap-1 rounded-full px-1.5 py-0.5 text-[10px] font-bold tracking-[0.08em]",
                isMove
                  ? "bg-[var(--color-pastel-mint)] text-[var(--color-pastel-mint-ink)]"
                  : "bg-[var(--color-pastel-peach)] text-[var(--color-pastel-peach-ink)]",
              )}
            >
              {mission.type}
            </span>
            {mission.category && (
              <span className="rounded-full bg-white/55 px-1.5 py-0.5 font-mono text-[10px] text-[var(--color-ink-500)] dark:bg-white/[0.06]">
                {mission.category}
              </span>
            )}
            {mission.blockingType && (
              <span className="rounded-full bg-[var(--color-amber-soft)] px-1.5 py-0.5 text-[10px] font-semibold text-[#8a4a07] dark:text-[var(--color-amber)]">
                {mission.blockingType}
              </span>
            )}
          </div>

          <div className="mt-2 flex flex-wrap items-center gap-2 text-[12px]">
            {isMove ? (
              <>
                {mission.mapId != null && (
                  <Pill icon={<MapIcon className="h-3 w-3" />}>
                    map <span className="font-mono">{mission.mapId}</span>
                  </Pill>
                )}
                {mission.stationId != null && (
                  <Pill icon={<Tag className="h-3 w-3" />}>
                    station <span className="font-mono">{mission.stationId}</span>
                  </Pill>
                )}
                {mission.mapId == null && mission.stationId == null && (
                  <span className="text-[11.5px] italic text-[var(--color-ink-400)]">
                    Target undefined
                  </span>
                )}
              </>
            ) : (
              <>
                {mission.actionTemplateName ? (
                  <Pill icon={<ArrowRight className="h-3 w-3" />} tone="brand">
                    <span className="font-mono font-semibold">
                      {mission.actionTemplateName}
                    </span>
                  </Pill>
                ) : mission.actionType ? (
                  <Pill icon={<ArrowRight className="h-3 w-3" />} tone="amber">
                    inline · <span className="font-mono">{mission.actionType}</span>
                  </Pill>
                ) : (
                  <span className="text-[11.5px] italic text-[var(--color-ink-400)]">
                    No action reference
                  </span>
                )}
              </>
            )}
          </div>

          {mission.actionParameters && mission.actionParameters.length > 0 && (
            <div className="mt-2 flex flex-wrap gap-1">
              {mission.actionParameters.map((p, i) => (
                <span
                  key={`${p.key}-${i}`}
                  className="inline-flex items-center gap-1 rounded-[6px] bg-[var(--color-ink-100)]/60 px-1.5 py-0.5 font-mono text-[10.5px] dark:bg-white/[0.06]"
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
      </div>
    </motion.li>
  );
}

function Pill({
  icon,
  children,
  tone = "ink",
}: {
  icon: React.ReactNode;
  children: React.ReactNode;
  tone?: "ink" | "brand" | "amber";
}) {
  const tones = {
    ink: "bg-white/65 text-[var(--color-ink-700)] dark:bg-white/[0.06] dark:text-[var(--color-ink-700)]",
    brand:
      "bg-[var(--color-pastel-sky)] text-[var(--color-pastel-sky-ink)]",
    amber: "bg-[var(--color-amber-soft)] text-[#8a4a07] dark:text-[var(--color-amber)]",
  };
  return (
    <span
      className={cn(
        "inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-[11.5px] font-semibold",
        tones[tone],
      )}
    >
      <span className="text-current opacity-70">{icon}</span>
      {children}
    </span>
  );
}

// ── Small parts ────────────────────────────────────────────────────────

function hasBinding(t: OrderTemplateDto): boolean {
  return !!(
    t.appointVehicleKey ||
    t.appointVehicleName ||
    t.appointVehicleGroupKey ||
    t.appointVehicleGroupName ||
    t.appointQueueWaitArea
  );
}

function PriorityBadge({ value }: { value: number }) {
  const tone =
    value >= 8
      ? "bg-[var(--color-coral)]/15 text-[var(--color-coral)]"
      : value >= 5
        ? "bg-[var(--color-amber-soft)] text-[#8a4a07] dark:text-[var(--color-amber)]"
        : "bg-[var(--color-pastel-sky)] text-[var(--color-pastel-sky-ink)]";
  return (
    <span
      className={cn(
        "inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-[10.5px] font-bold tracking-[0.08em]",
        tone,
      )}
    >
      Priority {value}
    </span>
  );
}

function StructureBadge({ type }: { type: string }) {
  const isParallel = type.toLowerCase() === "parallel";
  return (
    <span
      className={cn(
        "inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-[10.5px] font-bold tracking-[0.08em]",
        isParallel
          ? "bg-[var(--color-pastel-lavender)] text-[var(--color-pastel-lavender-ink)]"
          : "bg-[var(--color-pastel-mint)] text-[var(--color-pastel-mint-ink)]",
      )}
    >
      {type}
    </span>
  );
}

function BindingRow({
  icon,
  label,
  value,
  sub,
}: {
  icon: React.ReactNode;
  label: string;
  value: string;
  sub?: string | null;
}) {
  return (
    <div className="rounded-[var(--radius-sm)] border border-white/70 bg-white/55 px-3 py-2 dark:border-white/[0.06] dark:bg-white/[0.04]">
      <div className="flex items-center gap-1.5 text-[10.5px] font-semibold uppercase tracking-[0.1em] text-[var(--color-ink-400)]">
        <span className="text-[var(--color-ink-400)]">{icon}</span>
        {label}
      </div>
      <div className="mt-1 truncate text-[13px] font-semibold text-[var(--color-ink-900)]">
        {value}
      </div>
      {sub && (
        <div className="mt-0.5 truncate font-mono text-[10.5px] text-[var(--color-ink-500)]">
          {sub}
        </div>
      )}
    </div>
  );
}

function Section({
  title,
  children,
}: {
  title: string;
  children: React.ReactNode;
}) {
  return (
    <section>
      <h3 className="mb-2 text-[10.5px] font-semibold uppercase tracking-[0.14em] text-[var(--color-ink-400)]">
        {title}
      </h3>
      {children}
    </section>
  );
}

function MetaRow({
  icon,
  label,
  value,
}: {
  icon: React.ReactNode;
  label: string;
  value: React.ReactNode;
}) {
  return (
    <div className="flex items-center justify-between gap-3">
      <dt className="flex items-center gap-1.5 text-[11.5px] font-medium uppercase tracking-[0.08em] text-[var(--color-ink-500)]">
        <span className="text-[var(--color-ink-400)]">{icon}</span>
        {label}
      </dt>
      <dd className="text-right text-[var(--color-ink-800)]">{value}</dd>
    </div>
  );
}

function ActiveDot({ active }: { active: boolean }) {
  return (
    <span
      className={cn(
        "relative inline-flex h-1.5 w-1.5 rounded-full",
        active ? "bg-[var(--color-success)]" : "bg-[var(--color-ink-300)]",
      )}
    >
      {active && (
        <motion.span
          className="absolute inset-0 rounded-full bg-[var(--color-success)]"
          animate={{ scale: [1, 2.2, 1], opacity: [0.6, 0, 0.6] }}
          transition={{ duration: 1.8, repeat: Infinity, ease: "easeOut" }}
        />
      )}
    </span>
  );
}

const DRAWER_STYLES = {
  primary:
    "bg-[var(--color-brand-900)] text-white shadow-[0_4px_16px_-6px_rgba(15,23,42,0.45)] hover:shadow-[0_6px_20px_-6px_rgba(15,23,42,0.55)] dark:bg-[var(--color-brand-500)]",
  secondary:
    "bg-[var(--color-success-soft)] text-[var(--color-success)] hover:brightness-95",
  ghost:
    "text-[var(--color-ink-600)] hover:bg-white/55 dark:hover:bg-white/[0.06]",
} as const;

function DrawerAction({
  icon,
  children,
  onClick,
  disabled,
  variant,
}: {
  icon: React.ReactNode;
  children: React.ReactNode;
  onClick: () => void;
  disabled?: boolean;
  variant: keyof typeof DRAWER_STYLES;
}) {
  return (
    <motion.button
      type="button"
      whileTap={{ scale: 0.96 }}
      onClick={onClick}
      disabled={disabled}
      className={cn(
        "inline-flex cursor-pointer items-center gap-1.5 rounded-full px-3.5 py-2 text-[12px] font-semibold transition-all disabled:cursor-not-allowed disabled:opacity-50",
        DRAWER_STYLES[variant],
      )}
    >
      {icon}
      {children}
    </motion.button>
  );
}

function DrawerLink({
  icon,
  children,
  href,
  variant,
}: {
  icon: React.ReactNode;
  children: React.ReactNode;
  href: string;
  variant: keyof typeof DRAWER_STYLES;
}) {
  return (
    <Link
      href={href}
      className={cn(
        "inline-flex items-center gap-1.5 rounded-full px-3.5 py-2 text-[12px] font-semibold transition-all",
        DRAWER_STYLES[variant],
      )}
    >
      {icon}
      {children}
    </Link>
  );
}
