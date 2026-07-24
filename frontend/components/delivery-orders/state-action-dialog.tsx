"use client";

import { Ban, PauseCircle, PlayCircle, X } from "lucide-react";
import { AnimatePresence, motion } from "motion/react";
import { OverlayBackdrop } from "@/components/primitives/overlay-backdrop";
import { useEffect, useState } from "react";
import { cn } from "@/lib/utils";

// Captures the actor + (optional) reason for hold / release / reject —
// the three lifecycle moves that go through a free-text audit field.
// One component, three variants keeps the dialog visuals consistent
// and avoids three near-identical files.

export type StateActionVariant = "hold" | "release" | "reject" | "abandon";

type VariantConfig = {
  title: string;
  blurb: string;
  actorLabel: string;
  actorRequired: boolean;
  reasonLabel: string;
  reasonRequired: boolean;
  reasonPlaceholder: string;
  submitLabel: string;
  icon: React.ReactNode;
  bubbleClass: string;
  submitClass: string;
};

const CONFIG: Record<StateActionVariant, VariantConfig> = {
  hold: {
    title: "Hold order",
    blurb:
      "Holding pauses planning + dispatch. The order stays addressable in the system and can be Released back to Confirmed when ready.",
    actorLabel: "Held by",
    actorRequired: false,
    reasonLabel: "Reason",
    reasonRequired: true,
    reasonPlaceholder: "e.g. awaiting hazmat documentation",
    submitLabel: "Hold",
    icon: <PauseCircle className="h-4 w-4" strokeWidth={2.4} />,
    bubbleClass:
      "bg-[var(--color-amber-soft)] text-[var(--color-amber)]",
    submitClass:
      "bg-[var(--color-amber-soft)] text-[var(--color-amber)] hover:bg-[var(--color-amber-soft)]/80",
  },
  release: {
    title: "Release held order",
    blurb:
      "Release returns the order to Confirmed and re-fires planning. Use this once the blocker that caused the hold is resolved.",
    actorLabel: "Released by",
    actorRequired: false,
    reasonLabel: "Note",
    reasonRequired: false,
    reasonPlaceholder: "Optional — context for the audit log",
    submitLabel: "Release",
    icon: <PlayCircle className="h-4 w-4" strokeWidth={2.4} />,
    bubbleClass:
      "bg-[var(--color-success-soft)] text-[var(--color-success)]",
    submitClass:
      "bg-[var(--color-success)] text-white hover:shadow-[0_14px_36px_-12px_rgba(16,185,129,0.5)]",
  },
  reject: {
    title: "Reject order",
    blurb:
      "Rejecting is terminal — the order cannot be re-confirmed. Prefer Hold if the issue may be recoverable.",
    actorLabel: "Rejected by",
    actorRequired: false,
    reasonLabel: "Reason",
    reasonRequired: true,
    reasonPlaceholder: "e.g. duplicate submission; superseded by DO-…",
    submitLabel: "Reject",
    icon: <Ban className="h-4 w-4" strokeWidth={2.4} />,
    bubbleClass: "bg-[var(--color-coral-soft)] text-[var(--color-coral)]",
    submitClass:
      "bg-[var(--color-coral-soft)] text-[var(--color-coral)] hover:bg-[var(--color-coral-soft)]/80",
  },
  // Phase b11 escape hatch — close out an order stranded at an in-flight
  // status with no active Trip remaining. Backend validates BOTH (in-flight,
  // 0 active trips); UI only surfaces the action when both hold.
  abandon: {
    title: "Abandon stuck order",
    blurb:
      "Marks a stranded order Cancelled when every Trip has ended (typically all Cancelled). Items follow the order to a terminal state. Use this when the auto-cascade didn't fire — e.g. legacy data from before the cascade was added.",
    actorLabel: "Abandoned by",
    actorRequired: true,
    reasonLabel: "Reason",
    reasonRequired: true,
    reasonPlaceholder: "e.g. cleanup of legacy stuck Dispatched orders",
    submitLabel: "Abandon",
    icon: <Ban className="h-4 w-4" strokeWidth={2.4} />,
    bubbleClass:
      "bg-[var(--color-amber-soft)] text-[var(--color-amber)]",
    submitClass:
      "bg-[var(--color-amber-soft)] text-[var(--color-amber)] hover:bg-[var(--color-amber-soft)]/80",
  },
};

export function StateActionDialog({
  variant,
  orderRef,
  currentUser,
  open,
  onClose,
  onConfirm,
  busy,
  error,
}: {
  variant: StateActionVariant | null;
  orderRef: string | null;
  currentUser: string | null;
  open: boolean;
  onClose: () => void;
  onConfirm: (input: { actor: string; reason: string }) => Promise<void> | void;
  busy?: boolean;
  error?: string | null;
}) {
  const cfg = variant ? CONFIG[variant] : null;
  const [reason, setReason] = useState("");
  const [actor, setActor] = useState(currentUser ?? "");

  useEffect(() => {
    if (open) {
      setReason("");
      setActor(currentUser ?? "");
    }
  }, [open, currentUser, variant]);

  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => e.key === "Escape" && onClose();
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [open, onClose]);

  const canSubmit =
    !!cfg &&
    !busy &&
    (!cfg.actorRequired || actor.trim().length > 0) &&
    (!cfg.reasonRequired || reason.trim().length > 0);

  return (
    <>
      {/* State-driven backdrop — see OverlayBackdrop for the stuck-exit
          rationale. */}
      <OverlayBackdrop
        open={open && !!cfg}
        onClick={onClose}
        className="z-[80] bg-[var(--color-ink-900)]/50 backdrop-blur-sm"
      />
      <AnimatePresence>
        {open && cfg && (
          <motion.div
            key="state-action-dialog-panel"
            initial={{ opacity: 0, scale: 0.96, y: 8 }}
            animate={{ opacity: 1, scale: 1, y: 0 }}
            exit={{ opacity: 0, scale: 0.96 }}
            transition={{ duration: 0.22 }}
            className={cn(
              "fixed left-1/2 top-1/2 z-[90] w-[min(480px,calc(100vw-32px))] -translate-x-1/2 -translate-y-1/2",
              "rounded-2xl bg-[var(--color-surface)] shadow-[0_30px_80px_-20px_rgba(15,23,42,0.5)] dark:bg-[var(--color-surface)]",
            )}
            role="dialog"
            aria-modal="true"
          >
            <header className="flex items-start justify-between gap-3 border-b border-[var(--color-ink-100)] px-5 py-4 dark:border-white/[0.06]">
              <div className="flex items-center gap-2.5">
                <span
                  className={cn(
                    "inline-flex h-9 w-9 items-center justify-center rounded-full",
                    cfg.bubbleClass,
                  )}
                >
                  {cfg.icon}
                </span>
                <div>
                  <h2 className="text-[15px] font-semibold text-[var(--color-ink-900)]">
                    {cfg.title}
                  </h2>
                  {orderRef && (
                    <p className="mt-0.5 font-mono text-[11px] text-[var(--color-ink-500)]">
                      {orderRef}
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

            <form
              onSubmit={async (e) => {
                e.preventDefault();
                if (!canSubmit) return;
                await onConfirm({ actor: actor.trim(), reason: reason.trim() });
              }}
              className="space-y-4 px-5 py-4"
            >
              <p
                className={cn(
                  "rounded-lg px-3 py-2.5 text-[12px] leading-relaxed",
                  cfg.bubbleClass,
                )}
              >
                {cfg.blurb}
              </p>

              <label className="block">
                <span className="text-[10.5px] font-semibold uppercase tracking-[0.06em] text-[var(--color-ink-500)]">
                  {cfg.actorLabel}
                  {!cfg.actorRequired && (
                    <span className="ml-1 normal-case tracking-normal text-[var(--color-ink-400)]">
                      · optional
                    </span>
                  )}
                </span>
                <input
                  type="text"
                  value={actor}
                  onChange={(e) => setActor(e.target.value)}
                  placeholder="e.g. ops-lead-01"
                  className="mt-1 w-full rounded-lg border border-[var(--color-ink-100)] bg-[var(--color-surface)] px-3 py-2 text-[13px] text-[var(--color-ink-900)] focus:border-[var(--color-brand-500)] focus:outline-none focus:ring-2 focus:ring-[var(--color-brand-500)]/20 dark:border-white/[0.06] dark:bg-white/[0.02]"
                  required={cfg.actorRequired}
                />
              </label>

              <label className="block">
                <span className="text-[10.5px] font-semibold uppercase tracking-[0.06em] text-[var(--color-ink-500)]">
                  {cfg.reasonLabel}
                  {!cfg.reasonRequired && (
                    <span className="ml-1 normal-case tracking-normal text-[var(--color-ink-400)]">
                      · optional
                    </span>
                  )}
                </span>
                <textarea
                  value={reason}
                  onChange={(e) => setReason(e.target.value)}
                  placeholder={cfg.reasonPlaceholder}
                  rows={3}
                  className="mt-1 w-full resize-none rounded-lg border border-[var(--color-ink-100)] bg-[var(--color-surface)] px-3 py-2 text-[13px] text-[var(--color-ink-900)] focus:border-[var(--color-brand-500)] focus:outline-none focus:ring-2 focus:ring-[var(--color-brand-500)]/20 dark:border-white/[0.06] dark:bg-white/[0.02]"
                  required={cfg.reasonRequired}
                />
              </label>

              {error && (
                <div className="rounded-lg bg-[var(--color-coral-soft)] px-3 py-2 text-[11.5px] font-medium text-[var(--color-coral)]">
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
                      ? cfg.submitClass
                      : "cursor-not-allowed bg-[var(--color-ink-100)] text-[var(--color-ink-400)] dark:bg-white/[0.04]",
                  )}
                >
                  <span className={cn(busy && "animate-spin")}>{cfg.icon}</span>
                  {cfg.submitLabel}
                </button>
              </div>
            </form>
          </motion.div>
        )}
      </AnimatePresence>
    </>
  );
}
