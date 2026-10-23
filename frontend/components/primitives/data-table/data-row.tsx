"use client";

import { motion } from "motion/react";
import type { KeyboardEvent, ReactNode } from "react";
import { cn } from "@/lib/utils";
import { T, stagger } from "@/lib/motion";
import { useRowEntrance } from "./row-group";

/**
 * Clickable table row with keyboard + a11y baked in.
 *
 * Every consumer used to set `onClick` on `<motion.tr>` directly — which
 * worked for mouse users but left keyboard users stranded (no Tab focus,
 * no Enter handler, no focus ring). The audit flagged this as a CRITICAL
 * accessibility issue across every transactional table. Centralising it
 * here means future tables pick up the fix for free.
 *
 * The `delayIndex` prop drives a short staggered fade-in (capped by
 * `stagger()`). Inside a RowGroup / `DataTableBody signature=…` it plays
 * on the table's first load only; after that the group fades as a whole.
 * No exit animation: rows used to fade out for 0.18s while the new page
 * faded in, so the table was briefly two pages tall.
 *
 * `flash` paints the one-shot `.row-flash` highlight (see
 * useFlashOnChange) when the row changed under the operator's eyes.
 */
export function DataRow({
  onClick,
  selected = false,
  disabled = false,
  delayIndex = 0,
  flash = false,
  className,
  children,
}: {
  onClick?: () => void;
  selected?: boolean;
  /** Visually dim the row without removing it (e.g. inactive templates). */
  disabled?: boolean;
  delayIndex?: number;
  flash?: boolean;
  className?: string;
  children: ReactNode;
}) {
  const interactive = !!onClick;
  const entrance = useRowEntrance();

  const handleKeyDown = (e: KeyboardEvent<HTMLTableRowElement>) => {
    if (!interactive) return;
    if (e.key === "Enter" || e.key === " ") {
      // Don't hijack Space/Enter when the focused element is something
      // else (e.g. a button inside the row); only react when the row
      // itself owns focus.
      if (e.currentTarget !== e.target) return;
      e.preventDefault();
      onClick!();
    }
  };

  return (
    <motion.tr
      // No `layout` here on purpose: list pages refetch on SignalR hints
      // while robots are active, and a 400ms position tween lets rows
      // slide out from under an in-flight click (mousedown and mouseup
      // land on different rows → the browser never fires click). Reorders
      // snap instantly instead, keeping rows hit-testable.
      initial={entrance ? { opacity: 0, y: 4 } : false}
      animate={{ opacity: 1, y: 0 }}
      transition={{ ...T.fast, delay: stagger(delayIndex) }}
      onClick={interactive ? onClick : undefined}
      onKeyDown={handleKeyDown}
      tabIndex={interactive ? 0 : undefined}
      // No role="button": on a <tr> it replaced the row role, so screen
      // readers stopped reading cells against their column headers, and it
      // nested a "button" around the id cell and row-menu buttons. The row
      // stays focusable and Enter/Space still open it (review M20).
      className={cn(
        "group border-t border-[var(--color-ink-100)]/60 dark:border-white/[0.04]",
        "transition-colors duration-150",
        interactive && "cursor-pointer hover:bg-white/40 dark:hover:bg-white/[0.03]",
        // Focus fill needs its own dark shade: white/60 under the dark
        // theme's near-white text made the focused row unreadable.
        interactive &&
          "focus-visible:outline-none focus-visible:bg-white/60 dark:focus-visible:bg-white/[0.06] focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-[var(--color-brand-500)]",
        selected &&
          "bg-[var(--color-pastel-sky)]/40 dark:bg-[var(--color-pastel-sky)]/60",
        disabled && "opacity-70",
        flash && "row-flash",
        className,
      )}
    >
      {children}
    </motion.tr>
  );
}
