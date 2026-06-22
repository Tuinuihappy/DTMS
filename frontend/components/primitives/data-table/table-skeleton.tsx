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

/**
 * Inline skeleton rows for tables that want to keep their existing
 * header visible while loading. Renders N `<tr><td colSpan>` rows
 * with the canonical pulsing animation — drop directly inside a
 * `<DataTableBody>` / `<tbody>`.
 *
 * Used by report tables, where the header carries column-name context
 * that should stay visible even while the data fetch is in flight.
 */
export function TableSkeletonRows({
  rows = 3,
  colSpan,
}: {
  rows?: number;
  colSpan: number;
}) {
  return (
    <>
      {Array.from({ length: rows }).map((_, i) => (
        <tr key={i} aria-hidden>
          <td colSpan={colSpan} className="px-3 py-2">
            <motion.div
              initial={{ opacity: 0 }}
              animate={{ opacity: [0.4, 0.7, 0.4] }}
              transition={{ duration: 1.4, repeat: Infinity, delay: i * 0.06 }}
              className="h-6 rounded-md bg-[var(--color-ink-100)] dark:bg-white/[0.04]"
            />
          </td>
        </tr>
      ))}
    </>
  );
}
