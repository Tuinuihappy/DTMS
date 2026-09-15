"use client";

import { motion } from "motion/react";
import { cn } from "@/lib/utils";

/**
 * Full-screen dimming backdrop for drawers and modal dialogs.
 *
 * Deliberately NOT managed by AnimatePresence. Field repro (2026-07-24,
 * trips page): rapid open/close cycles can strand an exiting
 * AnimatePresence child in the DOM — an invisible `fixed inset-0` div
 * that swallows every click on the page until a hard refresh. Exiting
 * children are frozen with their last-rendered props, so nothing can
 * rescue them after the fact.
 *
 * This component instead stays permanently mounted: opacity animates
 * with `open`, but `pointer-events` follows React state directly. The
 * element is always in the owner's tree, so the class always updates —
 * a glitched fade can at worst leave a cosmetic tint, never a click
 * blocker.
 *
 * Render it as a SIBLING before your `<AnimatePresence>` panel, passing
 * the same z-index / tint / blur classes the old motion backdrop used:
 *
 *   <OverlayBackdrop
 *     open={!!orderId}
 *     onClick={onClose}
 *     className="z-40 bg-[var(--color-ink-900)]/40 backdrop-blur-sm"
 *   />
 *   <AnimatePresence>
 *     {orderId && <motion.aside key="order-drawer-panel" …>…</motion.aside>}
 *   </AnimatePresence>
 */
export function OverlayBackdrop({
  open,
  onClick,
  className,
  ...rest
}: {
  open: boolean;
  /** Usually the close handler; omit for dialogs that must not close on
   *  outside click. Only wired while open. */
  onClick?: () => void;
  /** z-index, tint and blur — keep whatever the overlay used before. */
  className?: string;
} & Record<`data-${string}`, string>) {
  return (
    <motion.div
      initial={false}
      animate={{ opacity: open ? 1 : 0 }}
      transition={{ duration: 0.25 }}
      onClick={open ? onClick : undefined}
      aria-hidden={!open}
      className={cn("fixed inset-0", !open && "pointer-events-none", className)}
      {...rest}
    />
  );
}
