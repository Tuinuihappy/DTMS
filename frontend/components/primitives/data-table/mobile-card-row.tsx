"use client";

import { motion } from "motion/react";
import type { KeyboardEvent, ReactNode } from "react";
import { cn } from "@/lib/utils";
import { T, stagger } from "@/lib/motion";
import { useRowEntrance } from "./row-group";

/**
 * Mobile card-layout counterpart to <DataRow>. Used inside the
 * `md:hidden` card list that several tables render alongside their
 * desktop `<table>` view. Same a11y guarantees as DataRow — keyboard
 * activation, focus ring, optional selected/disabled styling — but
 * renders a `<motion.div>` so it can live outside a `<tbody>` element.
 *
 * The Phase B/C/D migrations gave keyboard nav to every table row but
 * the mobile card layouts kept raw `<motion.div onClick>` (interactive
 * div with no keyboard handler — flagged in the audit as the last
 * a11y gap). This primitive closes that.
 *
 * Motion matches DataRow: short entrance on first load only (inside a
 * RowGroup), no exit, `flash` for the realtime-change highlight.
 */
export function MobileCardRow({
  onClick,
  selected = false,
  disabled = false,
  delayIndex = 0,
  flash = false,
  ariaLabel,
  className,
  children,
}: {
  onClick?: () => void;
  selected?: boolean;
  /** Visually dim the card without removing it (e.g. inactive templates). */
  disabled?: boolean;
  delayIndex?: number;
  flash?: boolean;
  /** Forwarded as aria-label — useful so screen readers announce the row
   *  context (e.g. "Order DEMO-001 — Submitted") even when the visible
   *  content is a stack of badges and icons. */
  ariaLabel?: string;
  className?: string;
  children: ReactNode;
}) {
  const interactive = !!onClick;
  const entrance = useRowEntrance();

  const handleKeyDown = (e: KeyboardEvent<HTMLDivElement>) => {
    if (!interactive) return;
    if (e.key === "Enter" || e.key === " ") {
      // Only react when the card itself owns focus — inner buttons /
      // links / checkboxes keep their normal Enter/Space behaviour.
      if (e.currentTarget !== e.target) return;
      e.preventDefault();
      onClick!();
    }
  };

  return (
    <motion.div
      // No `layout` here on purpose — same rationale as DataRow: list
      // pages refetch on SignalR hints, and a position tween lets cards
      // slide out from under an in-flight tap. Reorders snap instantly.
      initial={entrance ? { opacity: 0, y: 4 } : false}
      animate={{ opacity: 1, y: 0 }}
      transition={{ ...T.fast, delay: stagger(delayIndex) }}
      onClick={interactive ? onClick : undefined}
      onKeyDown={handleKeyDown}
      tabIndex={interactive ? 0 : undefined}
      role={interactive ? "button" : undefined}
      aria-label={ariaLabel}
      className={cn(
        "px-4 py-4 transition-colors",
        interactive && "cursor-pointer active:bg-white/40 dark:active:bg-white/[0.03]",
        interactive &&
          "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-[var(--color-brand-500)]",
        selected &&
          "bg-[var(--color-pastel-sky)]/40 dark:bg-[var(--color-pastel-sky)]/60",
        disabled && "opacity-70",
        flash && "row-flash",
        className,
      )}
    >
      {children}
    </motion.div>
  );
}
