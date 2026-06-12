"use client";

import { motion } from "motion/react";
import { cn } from "@/lib/utils";

// Phase P0.F4 — Reusable vertical timeline component shared across
// projection-backed views (status history, activity feed, audit trail).
// Consumer provides the entries; this component owns layout, animation,
// connector line, and empty/loading states.
//
// Design choices:
//   - Newest-first by default (caller may reverse before passing).
//   - Each entry's `dotTone` semantically colors the marker (success/
//     warning/error/info/neutral) — matches the wider design system.
//   - Animations use stagger so a 50-entry timeline feels natural without
//     blocking the first paint (each item ~0.2s reveal, delay = i * 0.03).

export type TimelineEntry = {
  id: string;
  /** Top line — e.g. "Confirmed → Planning" or "Trip cancelled" */
  title: React.ReactNode;
  /** Optional secondary line — e.g. "by ops-01 · reason: vendor rejected" */
  subtitle?: React.ReactNode;
  /** ISO string or Date — rendered as localized time + relative chip */
  occurredAt: string | Date;
  dotTone?: "success" | "warning" | "error" | "info" | "neutral";
  /** Optional adornment to the right of the row (e.g. action button) */
  trailing?: React.ReactNode;
};

export function TimelineView({
  entries,
  loading,
  emptyMessage = "No history yet.",
  className,
}: {
  entries: TimelineEntry[];
  loading?: boolean;
  emptyMessage?: string;
  className?: string;
}) {
  if (loading) {
    return (
      <div className={cn("space-y-2", className)}>
        {Array.from({ length: 4 }).map((_, i) => (
          <div
            key={i}
            className="h-14 animate-pulse rounded-xl bg-[var(--color-ink-100)] dark:bg-white/[0.05]"
          />
        ))}
      </div>
    );
  }

  if (entries.length === 0) {
    return (
      <p className={cn("text-[12.5px] text-[var(--color-ink-500)]", className)}>
        {emptyMessage}
      </p>
    );
  }

  return (
    <ol className={cn("relative space-y-3 pl-6", className)}>
      {/* Vertical thread connecting all dots — anchored to the dot column,
          fades at the bottom so the last entry doesn't look truncated. */}
      <span
        aria-hidden
        className="absolute left-[7px] top-1 bottom-4 w-px bg-gradient-to-b from-[var(--color-ink-200)] via-[var(--color-ink-100)] to-transparent dark:from-white/[0.12] dark:via-white/[0.06]"
      />
      {entries.map((entry, i) => (
        <TimelineRow key={entry.id} entry={entry} index={i} />
      ))}
    </ol>
  );
}

function TimelineRow({ entry, index }: { entry: TimelineEntry; index: number }) {
  const dotClass = DOT_TONES[entry.dotTone ?? "neutral"];
  const occurredAt =
    entry.occurredAt instanceof Date ? entry.occurredAt : new Date(entry.occurredAt);

  return (
    <motion.li
      initial={{ opacity: 0, x: -6 }}
      animate={{ opacity: 1, x: 0 }}
      transition={{ duration: 0.32, delay: Math.min(index * 0.03, 0.4) }}
      className="relative"
    >
      {/* Dot marker — sits exactly on the vertical thread */}
      <span
        aria-hidden
        className={cn(
          "absolute -left-[22px] top-1.5 h-3 w-3 rounded-full ring-2 ring-[var(--color-surface)] dark:ring-[var(--color-surface)]",
          dotClass,
        )}
      />

      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0 flex-1">
          <div className="text-[13px] font-semibold text-[var(--color-ink-900)]">
            {entry.title}
          </div>
          {entry.subtitle && (
            <div className="mt-0.5 text-[11.5px] leading-relaxed text-[var(--color-ink-500)]">
              {entry.subtitle}
            </div>
          )}
          <div className="mt-1 flex items-center gap-2 text-[10.5px] font-mono text-[var(--color-ink-400)]">
            <span>{occurredAt.toLocaleString()}</span>
            <span aria-hidden>·</span>
            <span>{relativeFromNow(occurredAt)}</span>
          </div>
        </div>
        {entry.trailing && <div className="shrink-0">{entry.trailing}</div>}
      </div>
    </motion.li>
  );
}

const DOT_TONES: Record<NonNullable<TimelineEntry["dotTone"]>, string> = {
  success: "bg-[var(--color-success)]",
  warning: "bg-[var(--color-amber)]",
  error: "bg-[var(--color-coral)]",
  info: "bg-[var(--color-brand-500)]",
  neutral: "bg-[var(--color-ink-400)]",
};

function relativeFromNow(d: Date): string {
  const diff = Date.now() - d.getTime();
  const sign = diff >= 0 ? "" : "in ";
  const abs = Math.abs(diff);
  const min = Math.floor(abs / 60_000);
  if (min < 1) return diff >= 0 ? "just now" : "in moments";
  if (min < 60) return `${sign}${min}m${diff >= 0 ? " ago" : ""}`;
  const hr = Math.floor(min / 60);
  if (hr < 24) return `${sign}${hr}h${diff >= 0 ? " ago" : ""}`;
  const day = Math.floor(hr / 24);
  if (day < 7) return `${sign}${day}d${diff >= 0 ? " ago" : ""}`;
  return d.toLocaleDateString();
}
