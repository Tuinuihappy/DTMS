"use client";

import { motion } from "motion/react";
import { cn } from "@/lib/utils";

/**
 * Loading skeleton for data tables. Used in place of a spinner so the
 * layout doesn't shift on data arrival (CLS audit win). Renders inside
 * the glass shell so callers can drop it directly under their search/
 * filter chrome.
 */
export function TableSkeleton({
  rows = 6,
  label = "Loading…",
  className,
}: {
  rows?: number;
  /** Caption above the skeleton rows; matches the header treatment. */
  label?: string;
  className?: string;
}) {
  return (
    <div
      className={cn(
        "rounded-[var(--radius-xl)] glass p-2",
        className,
      )}
      role="status"
      aria-live="polite"
      aria-busy="true"
    >
      <div className="flex gap-3 px-3 py-3 text-[10.5px] font-semibold uppercase tracking-[0.1em] text-[var(--color-ink-400)]">
        <span>{label}</span>
      </div>
      <div className="space-y-2 px-2 pb-2">
        {Array.from({ length: rows }).map((_, i) => (
          <motion.div
            key={i}
            initial={{ opacity: 0 }}
            animate={{ opacity: [0.4, 0.7, 0.4] }}
            transition={{ duration: 1.4, repeat: Infinity, delay: i * 0.06 }}
            className="h-12 rounded-xl bg-[var(--color-ink-100)] dark:bg-white/[0.04]"
            aria-hidden
          />
        ))}
      </div>
    </div>
  );
}
