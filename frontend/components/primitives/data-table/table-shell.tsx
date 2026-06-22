"use client";

import type { ReactNode, ThHTMLAttributes } from "react";
import { cn } from "@/lib/utils";

// Density baked into the shell so every consumer sees the same row
// rhythm. Picked from the orders-table baseline that the audit flagged
// as the canonical look (py-3.5 for transactional, py-2.5 for compact
// drawer/report tables).
export type TableDensity = "compact" | "normal";

const TD_PADDING: Record<TableDensity, string> = {
  normal: "px-3 py-3.5",
  compact: "px-3 py-2.5",
};

const TH_PADDING: Record<TableDensity, string> = {
  normal: "px-3 py-3.5",
  compact: "px-3 py-2",
};

/**
 * Outer shell that gives every table the same glass card + horizontal
 * overflow behaviour. Mobile responsiveness is left to the caller —
 * tables that need a card fallback render their own `<div className="md:hidden">`
 * variant alongside.
 */
export function DataTableShell({
  children,
  className,
}: {
  children: ReactNode;
  className?: string;
}) {
  return (
    <div
      className={cn(
        "overflow-hidden rounded-[var(--radius-xl)] glass",
        className,
      )}
    >
      <div className="overflow-x-auto">
        <table className="w-full text-left">{children}</table>
      </div>
    </div>
  );
}

/**
 * Header row wrapper. Applies the canonical uppercase tracking the
 * audit standardised on (10.5px / tracking-[0.1em] / ink-400). Children
 * are individual `<TableTh>` or `<SortableTh>` cells.
 */
export function DataTableHead({ children }: { children: ReactNode }) {
  return (
    <thead>
      <tr className="text-[10.5px] font-semibold uppercase tracking-[0.1em] text-[var(--color-ink-400)]">
        {children}
      </tr>
    </thead>
  );
}

export function DataTableBody({ children }: { children: ReactNode }) {
  return <tbody>{children}</tbody>;
}

/**
 * Plain (non-sortable) header cell. Always sets `scope="col"` so screen
 * readers announce column headers correctly.
 */
export function TableTh({
  children,
  align = "left",
  density = "normal",
  className,
  ...rest
}: {
  children: ReactNode;
  align?: "left" | "right";
  density?: TableDensity;
} & Omit<ThHTMLAttributes<HTMLTableCellElement>, "scope" | "align">) {
  return (
    <th
      scope="col"
      className={cn(
        TH_PADDING[density],
        align === "right" && "text-right",
        className,
      )}
      {...rest}
    >
      {children}
    </th>
  );
}

/**
 * Data cell. Mirrors the padding scale of `TableTh` for vertical rhythm.
 * `colSpan` is plumbed through for inline empty-state rows (report
 * tables render "No data in this window" as a single spanning cell so
 * the header stays visible).
 */
export function TableTd({
  children,
  align = "left",
  density = "normal",
  colSpan,
  className,
}: {
  children: ReactNode;
  align?: "left" | "right";
  density?: TableDensity;
  colSpan?: number;
  className?: string;
}) {
  return (
    <td
      colSpan={colSpan}
      className={cn(
        TD_PADDING[density],
        align === "right" && "text-right",
        className,
      )}
    >
      {children}
    </td>
  );
}
