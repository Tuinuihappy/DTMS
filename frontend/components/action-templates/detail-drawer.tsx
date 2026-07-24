"use client";

import { AnimatePresence, motion } from "motion/react";
import { OverlayBackdrop } from "@/components/primitives/overlay-backdrop";
import {
  Activity,
  Clock,
  Copy,
  Hash,
  Pencil,
  Power,
  Trash2,
  User,
  Workflow,
  X,
} from "lucide-react";
import { cn } from "@/lib/utils";
import type { ActionTemplateDto } from "@/lib/api/action-templates";
import { DateTime } from "@/components/primitives/date-time";

export function ActionTemplateDrawer({
  open,
  template,
  busy = false,
  onClose,
  onEdit,
  onDuplicate,
  onToggleActive,
  onDelete,
}: {
  open: boolean;
  template: ActionTemplateDto | null;
  busy?: boolean;
  onClose: () => void;
  onEdit: (t: ActionTemplateDto) => void;
  onDuplicate: (t: ActionTemplateDto) => void;
  onToggleActive: (t: ActionTemplateDto) => void;
  onDelete: (t: ActionTemplateDto) => void;
}) {
  return (
    <>
      {/* State-driven backdrop — see OverlayBackdrop for the stuck-exit
          rationale. */}
      <OverlayBackdrop
        open={open && !!template}
        onClick={() => !busy && onClose()}
        className="z-40 bg-[var(--color-ink-900)]/40 backdrop-blur-sm"
      />
      <AnimatePresence>
        {open && template && (
          <motion.aside
            key="action-template-drawer-panel"
            initial={{ x: "100%" }}
            animate={{ x: 0 }}
            exit={{ x: "100%" }}
            transition={{ type: "spring", stiffness: 320, damping: 34 }}
            className="fixed inset-y-0 right-0 z-50 flex w-full max-w-md flex-col bg-[var(--color-canvas)]/95 backdrop-blur-2xl shadow-[-20px_0_60px_-20px_rgba(15,23,42,0.35)] dark:bg-[var(--color-canvas)]/90"
          >
            <div className="relative overflow-hidden">
              <div className="absolute inset-0 bg-gradient-to-br from-[var(--color-pastel-lavender)]/40 via-transparent to-[var(--color-pastel-sky)]/40 pointer-events-none" />
              <div className="relative flex items-start justify-between gap-3 px-6 pt-6 pb-5">
                <div className="flex items-start gap-3">
                  <span className="grid h-11 w-11 place-items-center rounded-[14px] bg-white/80 text-[var(--color-brand-900)] shadow-[inset_0_1px_0_rgba(255,255,255,0.9),0_6px_18px_-8px_rgba(15,23,42,0.25)] dark:bg-white/[0.08]">
                    <Workflow className="h-4 w-4" strokeWidth={2.1} />
                  </span>
                  <div className="min-w-0">
                    <div className="flex items-center gap-2">
                      <span className="text-[10.5px] font-semibold uppercase tracking-[0.14em] text-[var(--color-ink-400)]">
                        Action template
                      </span>
                      <ActiveDot active={template.isActive} />
                    </div>
                    <h2 className="font-display mt-1 truncate text-[1.45rem] font-semibold text-[var(--color-ink-900)]">
                      {template.actionName}
                    </h2>
                    <div className="mt-1.5 flex items-center gap-1.5">
                      <span
                        className={cn(
                          "rounded-full px-2 py-0.5 text-[10.5px] font-bold tracking-[0.08em]",
                          template.actionCategory === "Act"
                            ? "bg-[var(--color-pastel-peach)] text-[var(--color-pastel-peach-ink)]"
                            : "bg-[var(--color-pastel-mint)] text-[var(--color-pastel-mint-ink)]",
                        )}
                      >
                        {template.actionCategory.toUpperCase()}
                      </span>
                      <span className="rounded-full bg-white/55 px-2 py-0.5 font-mono text-[10.5px] font-semibold tracking-[0.02em] text-[var(--color-ink-700)] dark:bg-white/[0.06]">
                        {template.actionType}
                      </span>
                      <span className="text-[11px] text-[var(--color-ink-400)]">
                        {template.actionParameters.length} parameter
                        {template.actionParameters.length === 1 ? "" : "s"}
                      </span>
                    </div>
                  </div>
                </div>
                <button
                  type="button"
                  onClick={() => !busy && onClose()}
                  className="rounded-full p-2 text-[var(--color-ink-500)] transition-colors hover:bg-white/50 hover:text-[var(--color-ink-900)] dark:hover:bg-white/10"
                  aria-label="Close"
                >
                  <X className="h-4 w-4" strokeWidth={2.4} />
                </button>
              </div>
            </div>

            <div className="min-h-0 flex-1 overflow-y-auto px-6 py-5 space-y-6">
              <Section title="Parameters">
                {template.actionParameters.length === 0 ? (
                  <p className="text-[12.5px] italic text-[var(--color-ink-400)]">
                    No parameters defined.
                  </p>
                ) : (
                  <div className="space-y-1.5">
                    {template.actionParameters.map((p, i) => (
                      <motion.div
                        key={`${p.key}-${i}`}
                        initial={{ opacity: 0, x: 6 }}
                        animate={{ opacity: 1, x: 0 }}
                        transition={{ delay: i * 0.04, duration: 0.22 }}
                        className="flex items-center justify-between gap-3 rounded-[var(--radius-sm)] border border-white/70 bg-white/55 px-3 py-2 text-[12.5px] dark:border-white/[0.06] dark:bg-white/[0.04]"
                      >
                        <span className="font-mono text-[11.5px] uppercase tracking-[0.05em] text-[var(--color-ink-500)]">
                          {p.key}
                        </span>
                        <span className="font-mono text-[12.5px] font-semibold text-[var(--color-ink-900)]">
                          {p.value == null || p.value === ""
                            ? "—"
                            : String(p.value)}
                        </span>
                      </motion.div>
                    ))}
                  </div>
                )}
              </Section>

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

            <div className="shrink-0 border-t border-white/60 bg-white/30 px-6 py-4 dark:border-white/[0.06] dark:bg-white/[0.02]">
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
                <DrawerAction
                  variant="ghost"
                  icon={<Copy className="h-3.5 w-3.5" />}
                  onClick={() => onDuplicate(template)}
                  disabled={busy}
                >
                  Duplicate
                </DrawerAction>
                <DrawerAction
                  variant="primary"
                  icon={<Pencil className="h-3.5 w-3.5" />}
                  onClick={() => onEdit(template)}
                  disabled={busy}
                >
                  Edit
                </DrawerAction>
              </div>
            </div>
          </motion.aside>
        )}
      </AnimatePresence>
    </>
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
  variant: "primary" | "secondary" | "ghost";
}) {
  const base =
    "inline-flex items-center gap-1.5 rounded-full px-3.5 py-2 text-[12px] font-semibold transition-all disabled:cursor-not-allowed disabled:opacity-50";
  const styles =
    variant === "primary"
      ? "bg-[var(--color-brand-900)] text-white shadow-[0_4px_16px_-6px_rgba(15,23,42,0.45)] hover:shadow-[0_6px_20px_-6px_rgba(15,23,42,0.55)] dark:bg-[var(--color-brand-500)]"
      : variant === "secondary"
        ? "bg-[var(--color-success-soft)] text-[var(--color-success)] hover:brightness-95"
        : "text-[var(--color-ink-600)] hover:bg-white/55 dark:hover:bg-white/[0.06]";
  return (
    <motion.button
      type="button"
      whileTap={{ scale: 0.96 }}
      onClick={onClick}
      disabled={disabled}
      className={cn(base, styles)}
    >
      {icon}
      {children}
    </motion.button>
  );
}
