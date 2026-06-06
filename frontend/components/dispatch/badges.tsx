"use client";

import { ArrowDown, Repeat2 } from "lucide-react";
import { cn } from "@/lib/utils";
import type { TripStatus } from "@/lib/api/trips";

// Trip status → pastel tone. Mirrors the order-side palette so a busy
// dashboard stays glance-able. "Live" statuses get a pulsing dot.
type TripVisual = {
  label: string;
  tone: "ink" | "sky" | "peach" | "amber" | "success" | "coral";
  pulse?: boolean;
};

const TRIP_VISUAL: Record<TripStatus, TripVisual> = {
  Created: { label: "Created", tone: "sky", pulse: true },
  InProgress: { label: "In progress", tone: "peach", pulse: true },
  Paused: { label: "Paused", tone: "amber" },
  Completed: { label: "Completed", tone: "success" },
  Failed: { label: "Failed", tone: "coral" },
  Cancelled: { label: "Cancelled", tone: "ink" },
};

const TONE_BG: Record<TripVisual["tone"], string> = {
  ink: "bg-[var(--color-ink-100)] text-[var(--color-ink-700)]",
  sky: "bg-[var(--color-pastel-sky)] text-[var(--color-pastel-sky-ink)]",
  peach: "bg-[var(--color-pastel-peach)] text-[var(--color-pastel-peach-ink)]",
  amber: "bg-[var(--color-amber-soft)] text-[var(--color-amber)]",
  success: "bg-[var(--color-success-soft)] text-[var(--color-success)]",
  coral: "bg-[#fde0db] text-[var(--color-coral)] dark:bg-[#3a1a17]",
};

const TONE_DOT: Record<TripVisual["tone"], string> = {
  ink: "bg-[var(--color-ink-400)]",
  sky: "bg-[var(--color-brand-500)]",
  peach: "bg-[var(--color-amber)]",
  amber: "bg-[var(--color-amber)]",
  success: "bg-[var(--color-success)]",
  coral: "bg-[var(--color-coral)]",
};

export function TripStatusBadge({
  status,
  size = "sm",
}: {
  status: TripStatus;
  size?: "sm" | "md";
}) {
  const v = TRIP_VISUAL[status] ?? { label: status, tone: "ink" as const };
  const sizes = size === "md" ? "px-2.5 py-1 text-[11.5px]" : "px-2 py-[3px] text-[10.5px]";
  return (
    <span
      className={cn(
        "inline-flex items-center gap-1.5 rounded-full font-semibold uppercase tracking-[0.08em] whitespace-nowrap",
        sizes,
        TONE_BG[v.tone],
      )}
    >
      <span className="relative inline-flex h-1.5 w-1.5">
        <span className={cn("absolute inset-0 rounded-full", TONE_DOT[v.tone])} />
        {v.pulse && (
          <span
            className={cn(
              "absolute inset-0 rounded-full animate-ping opacity-60",
              TONE_DOT[v.tone],
            )}
          />
        )}
      </span>
      {v.label}
    </span>
  );
}

// Compact attempt indicator. "1" stays plain, retries get the loop icon.
export function AttemptBadge({ attempt }: { attempt: number }) {
  if (attempt <= 1) {
    return (
      <span className="font-mono text-[11px] font-semibold text-[var(--color-ink-500)]">
        #{attempt}
      </span>
    );
  }
  return (
    <span
      className="inline-flex items-center gap-1 rounded-md bg-[var(--color-pastel-lavender)] px-1.5 py-[3px] font-mono text-[10.5px] font-semibold uppercase tracking-[0.06em] text-[var(--color-pastel-lavender-ink)]"
      title={`Retry attempt #${attempt}`}
    >
      <Repeat2 className="h-3 w-3" strokeWidth={2.4} />
      Attempt {attempt}
    </span>
  );
}

// Per-mission state pip — used inline in the mission timeline.
export function MissionStateBadge({ state }: { state: string }) {
  const s = state.toUpperCase();
  const config = {
    PROCESSING: {
      label: "Processing",
      cls: "bg-[var(--color-pastel-sky)] text-[var(--color-pastel-sky-ink)]",
      pulse: true,
    },
    FINISHED: {
      label: "Finished",
      cls: "bg-[var(--color-success-soft)] text-[var(--color-success)]",
    },
    FAILED: {
      label: "Failed",
      cls: "bg-[#fde0db] text-[var(--color-coral)] dark:bg-[#3a1a17]",
    },
    CANCELED: {
      label: "Cancelled",
      cls: "bg-[var(--color-ink-100)] text-[var(--color-ink-700)]",
    },
    CANCELLED: {
      label: "Cancelled",
      cls: "bg-[var(--color-ink-100)] text-[var(--color-ink-700)]",
    },
  }[s] ?? {
    label: s,
    cls: "bg-[var(--color-ink-100)] text-[var(--color-ink-700)]",
  };
  const isPulsing = "pulse" in config && config.pulse === true;
  return (
    <span
      className={cn(
        "inline-flex items-center gap-1 rounded-full px-2 py-[2px] text-[10px] font-semibold uppercase tracking-[0.06em]",
        config.cls,
      )}
    >
      {isPulsing && (
        <span className="relative inline-flex h-1.5 w-1.5">
          <span className="absolute inset-0 rounded-full bg-current opacity-80" />
          <span className="absolute inset-0 rounded-full bg-current animate-ping opacity-50" />
        </span>
      )}
      {config.label}
    </span>
  );
}

// Renders an attempt → next-attempt chain navigator. Used in the trip
// drawer header so operators can jump between retries of the same trip.
export function RetryChainNav({
  attempt,
  previousAttemptId,
  onOpenPrevious,
}: {
  attempt: number;
  previousAttemptId: string | null;
  onOpenPrevious?: (id: string) => void;
}) {
  if (attempt <= 1 || !previousAttemptId) return null;
  return (
    <button
      type="button"
      onClick={() => onOpenPrevious?.(previousAttemptId)}
      className="inline-flex items-center gap-1.5 rounded-md bg-[var(--color-pastel-lavender)]/60 px-2 py-1 text-[11px] font-medium text-[var(--color-pastel-lavender-ink)] transition-colors hover:bg-[var(--color-pastel-lavender)]"
    >
      <ArrowDown className="h-3 w-3 rotate-180" strokeWidth={2.4} />
      Previous attempt
    </button>
  );
}
