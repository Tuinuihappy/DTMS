"use client";

import { ArrowUpDown, ChevronDown, ChevronUp } from "lucide-react";
import type { ReactNode } from "react";
import { cn } from "@/lib/utils";
import type { TableDensity } from "./table-shell";

const TH_PADDING: Record<TableDensity, string> = {
  normal: "px-3 py-3.5",
  compact: "px-3 py-2",
};

/**
 * Clickable column header that drives server-side sort. Inactive columns
 * always show a faint ArrowUpDown so the affordance is discoverable —
 * the audit flagged tables without this hint as the #1 reason operators
 * didn't realise columns were sortable.
 *
 * Sets `aria-sort` correctly for screen readers and exposes a verbose
 * `aria-label` (e.g. "Sort by Updated, currently descending") so the
 * action is clear without visual context.
 */
export function SortableTh<C extends string>({
  col,
  sortBy,
  sortDir,
  onSort,
  align = "left",
  density = "normal",
  children,
}: {
  col: C;
  sortBy: C;
  sortDir: "asc" | "desc";
  onSort: (col: C) => void;
  align?: "left" | "right";
  density?: TableDensity;
  children: ReactNode;
}) {
  const active = sortBy === col;
  const Icon = !active ? ArrowUpDown : sortDir === "asc" ? ChevronUp : ChevronDown;
  const label = typeof children === "string" ? children : col;

  return (
    <th
      scope="col"
      className={cn(TH_PADDING[density], align === "right" && "text-right")}
      aria-sort={active ? (sortDir === "asc" ? "ascending" : "descending") : "none"}
    >
      <button
        type="button"
        onClick={() => onSort(col)}
        aria-label={`Sort by ${label}${
          active ? `, currently ${sortDir === "asc" ? "ascending" : "descending"}` : ""
        }`}
        className={cn(
          "group inline-flex items-center gap-1 transition-colors duration-150",
          "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--color-brand-500)] focus-visible:ring-offset-2 rounded",
          align === "right" && "flex-row-reverse",
          active
            ? "text-[var(--color-ink-900)]"
            : "text-[var(--color-ink-400)] hover:text-[var(--color-ink-700)]",
        )}
      >
        <span>{children}</span>
        <Icon
          className={cn(
            "h-3 w-3 transition-opacity",
            active ? "opacity-100" : "opacity-50 group-hover:opacity-80",
          )}
          strokeWidth={2.4}
          aria-hidden
        />
      </button>
    </th>
  );
}
