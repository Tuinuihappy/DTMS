"use client";

import { motion } from "motion/react";
import type { KeyboardEvent, ReactNode } from "react";
import { cn } from "@/lib/utils";

/**
 * Clickable table row with keyboard + a11y baked in.
 *
 * Every consumer used to set `onClick` on `<motion.tr>` directly — which
 * worked for mouse users but left keyboard users stranded (no Tab focus,
 * no Enter handler, no focus ring). The audit flagged this as a CRITICAL
 * accessibility issue across every transactional table. Centralising it
 * here means future tables pick up the fix for free.
 *
 * The `delayIndex` prop drives the staggered fade-in animation (cap at
 * ~0.4s so a 50-row page doesn't take forever to settle).
 */
export function DataRow({
  onClick,
  selected = false,
  disabled = false,
  delayIndex = 0,
  className,
  children,
}: {
  onClick?: () => void;
  selected?: boolean;
  /** Visually dim the row without removing it (e.g. inactive templates). */
  disabled?: boolean;
  delayIndex?: number;
  className?: string;
  children: ReactNode;
}) {
  const interactive = !!onClick;

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
      layout
      initial={{ opacity: 0, y: 6 }}
      animate={{ opacity: 1, y: 0 }}
      exit={{ opacity: 0, transition: { duration: 0.18 } }}
      transition={{
        duration: 0.4,
        delay: Math.min(delayIndex * 0.025, 0.4),
        ease: [0.22, 1, 0.36, 1],
      }}
      onClick={interactive ? onClick : undefined}
      onKeyDown={handleKeyDown}
      tabIndex={interactive ? 0 : undefined}
      role={interactive ? "button" : undefined}
      className={cn(
        "group border-t border-[var(--color-ink-100)]/60 dark:border-white/[0.04]",
        "transition-colors duration-150",
        interactive && "cursor-pointer hover:bg-white/40 dark:hover:bg-white/[0.03]",
        interactive &&
          "focus-visible:outline-none focus-visible:bg-white/60 focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-[var(--color-brand-500)]",
        selected &&
          "bg-[var(--color-pastel-sky)]/40 dark:bg-[var(--color-pastel-sky)]/60",
        disabled && "opacity-70",
        className,
      )}
    >
      {children}
    </motion.tr>
  );
}
