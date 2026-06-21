"use client";

import { cn } from "@/lib/utils";
import type { JobStatus } from "@/lib/api/jobs";

// Job status → tone palette. Mirrors the Trip-side colors so the eye
// associates the same outcome across aggregates (Trip Failed + Job Failed
// → same coral). "Live" states get a pulsing dot.
type JobVisual = {
  label: string;
  tone: "ink" | "sky" | "peach" | "amber" | "success" | "coral" | "lavender";
  pulse?: boolean;
};

const JOB_VISUAL: Record<JobStatus, JobVisual> = {
  Created: { label: "Created", tone: "sky", pulse: true },
  Assigned: { label: "Assigned", tone: "amber" },
  Committed: { label: "Committed", tone: "amber" },
  Dispatched: { label: "Dispatched", tone: "peach", pulse: true },
  Executing: { label: "Executing", tone: "peach", pulse: true },
  // Phase #1 — distinct from Executing (no pulse, lavender tone) so ops
  // see at a glance that the robot is intentionally idle. Mirrors
  // Trip.Paused state via TripPausedJobConsumer.
  Paused: { label: "Paused", tone: "lavender" },
  Completed: { label: "Completed", tone: "success" },
  Failed: { label: "Failed", tone: "coral" },
  Cancelled: { label: "Cancelled", tone: "ink" },
};

const TONE_BG: Record<JobVisual["tone"], string> = {
  ink: "bg-[var(--color-ink-100)] text-[var(--color-ink-700)]",
  sky: "bg-[var(--color-pastel-sky)] text-[var(--color-pastel-sky-ink)]",
  peach: "bg-[var(--color-pastel-peach)] text-[var(--color-pastel-peach-ink)]",
  amber: "bg-[var(--color-amber-soft)] text-[var(--color-amber)]",
  success: "bg-[var(--color-success-soft)] text-[var(--color-success)]",
  coral: "bg-[var(--color-coral-soft)] text-[var(--color-coral)]",
  lavender: "bg-[var(--color-pastel-lavender,#e9e3ff)] text-[var(--color-pastel-lavender-ink,#5b4daf)] dark:bg-[var(--color-pastel-lavender)] dark:text-[var(--color-pastel-lavender-ink)]",
};

const TONE_DOT: Record<JobVisual["tone"], string> = {
  ink: "bg-[var(--color-ink-400)]",
  sky: "bg-[var(--color-brand-500)]",
  peach: "bg-[var(--color-amber)]",
  amber: "bg-[var(--color-amber)]",
  success: "bg-[var(--color-success)]",
  coral: "bg-[var(--color-coral)]",
  lavender: "bg-[var(--color-pastel-lavender-ink,#5b4daf)]",
};

export function JobStatusBadge({
  status,
  size = "sm",
}: {
  status: JobStatus;
  size?: "sm" | "md";
}) {
  const v = JOB_VISUAL[status] ?? { label: status, tone: "ink" as const };
  const sizes =
    size === "md" ? "px-2.5 py-1 text-[11.5px]" : "px-2 py-[3px] text-[10.5px]";
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
