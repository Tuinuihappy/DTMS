"use client";

import { Sparkles } from "lucide-react";
import { motion } from "motion/react";
import type { ComponentType, ReactNode } from "react";
import { cn } from "@/lib/utils";

/**
 * Three-variant empty state for data tables. Variant is chosen by the
 * caller (search > filter > no-data precedence) instead of inferred
 * from props so a table can use only a subset (e.g. report tables that
 * never have a search field).
 *
 * Designed around the orders-table baseline that the audit flagged as
 * the canonical look — gradient pastel tile, animated float, optional
 * CTA. Other tables get the same affordance without re-implementing.
 */
export type EmptyStateVariant = "no-search-match" | "no-filter-match" | "no-data";

interface EmptyStateProps {
  variant: EmptyStateVariant;
  title: string;
  body: ReactNode;
  /** Override the default Sparkles icon. */
  icon?: ComponentType<{ className?: string; strokeWidth?: number }>;
  /**
   * Primary action button. The most common pairing is
   * `{ label: "Clear filters", onClick: onClearFilters }` for the
   * `no-filter-match` variant.
   */
  action?: {
    label: string;
    onClick: () => void;
  };
  className?: string;
}

export function TableEmptyState({
  variant,
  title,
  body,
  icon: Icon = Sparkles,
  action,
  className,
}: EmptyStateProps) {
  return (
    <div
      className={cn(
        "rounded-[var(--radius-xl)] glass px-6 py-16 text-center",
        className,
      )}
      role="status"
      data-variant={variant}
    >
      <motion.div
        animate={{ y: [0, -4, 0] }}
        transition={{ duration: 3, repeat: Infinity, ease: "easeInOut" }}
        className="mx-auto grid h-16 w-16 place-items-center rounded-[20px] bg-gradient-to-br from-[var(--color-pastel-sky)] to-[var(--color-pastel-lavender)] text-[var(--color-brand-900)]"
        aria-hidden
      >
        <Icon className="h-6 w-6" strokeWidth={2} />
      </motion.div>
      <h3 className="font-display mt-5 text-lg font-semibold text-[var(--color-ink-900)]">
        {title}
      </h3>
      <p className="mx-auto mt-1 max-w-sm text-[13px] text-[var(--color-ink-500)]">
        {body}
      </p>
      {action && (
        <motion.button
          type="button"
          onClick={action.onClick}
          whileHover={{ y: -1 }}
          whileTap={{ scale: 0.97 }}
          className="mt-5 inline-flex items-center gap-1.5 rounded-full bg-[var(--color-brand-900)] px-4 py-2 text-[12px] font-semibold text-white transition-all hover:shadow-[0_14px_36px_-12px_rgba(15,23,42,0.6)] dark:bg-[var(--color-brand-500)]"
        >
          {action.label}
        </motion.button>
      )}
    </div>
  );
}

/**
 * Helper to pick the right variant from the common (search, hasFilters)
 * combo most tables expose. Tables with more nuanced state can pick
 * the variant themselves.
 */
export function resolveEmptyStateVariant(
  search: string,
  hasFilters: boolean,
): EmptyStateVariant {
  if (search.trim()) return "no-search-match";
  if (hasFilters) return "no-filter-match";
  return "no-data";
}
